/**
 * @file ModelImporter.cpp
 * @brief Importação Assimp + skinning em CPU para animações esqueléticas.
 */

#include "Renderer/ModelImporter.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include <assimp/Importer.hpp>
#include <assimp/postprocess.h>
#include <assimp/scene.h>
#include <algorithm>
#include <cmath>
#include <filesystem>
#include <unordered_map>

namespace VelvX {

std::vector<ImportedModel> ModelImporter::models_;
std::vector<ModelImporter::PlaybackState> ModelImporter::playback_;

namespace {

/** @brief aiMatrix4x4 (row-major) → glm (column-major). */
glm::mat4 toGlm(const aiMatrix4x4& m) {
    glm::mat4 result;
    for (int row = 0; row < 4; ++row) {
        for (int col = 0; col < 4; ++col) {
            result[col][row] = m[row][col];
        }
    }
    return result;
}

/** @brief aiVector3D → glm::vec3. */
glm::vec3 toGlm(const aiVector3D& v) {
    return glm::vec3(v.x, v.y, v.z);
}

/** @brief aiQuaternion → glm::quat. */
glm::quat toGlm(const aiQuaternion& q) {
    return glm::quat(q.w, q.x, q.y, q.z);
}

/** @brief Converte a hierarquia de nós. */
SceneNode convertNode(const aiNode* node) {
    SceneNode result;
    result.name = node->mName.C_Str();
    result.transform = toGlm(node->mTransformation);
    for (unsigned int i = 0; i < node->mNumMeshes; ++i) {
        result.meshIndices.push_back(static_cast<int>(node->mMeshes[i]));
    }
    for (unsigned int i = 0; i < node->mNumChildren; ++i) {
        result.children.push_back(convertNode(node->mChildren[i]));
    }
    return result;
}

/** @brief Extrai vértices, índices, ossos e material de uma malha. */
ImportedMesh convertMesh(const aiMesh* mesh) {
    ImportedMesh result;
    result.materialIndex = static_cast<int>(mesh->mMaterialIndex);
    result.vertices.reserve(mesh->mNumVertices);
    for (unsigned int i = 0; i < mesh->mNumVertices; ++i) {
        Vertex vertex;
        vertex.position = toGlm(mesh->mVertices[i]);
        if (mesh->HasNormals()) {
            vertex.normal = toGlm(mesh->mNormals[i]);
        }
        if (mesh->HasTextureCoords(0)) {
            vertex.uv = glm::vec2(mesh->mTextureCoords[0][i].x, mesh->mTextureCoords[0][i].y);
        }
        result.vertices.push_back(vertex);
    }
    for (unsigned int i = 0; i < mesh->mNumFaces; ++i) {
        const aiFace& face = mesh->mFaces[i];
        for (unsigned int j = 0; j < face.mNumIndices; ++j) {
            result.indices.push_back(face.mIndices[j]);
        }
    }
    for (unsigned int i = 0; i < mesh->mNumBones; ++i) {
        const aiBone* bone = mesh->mBones[i];
        ImportedBone imported;
        imported.name = bone->mName.C_Str();
        imported.offset = toGlm(bone->mOffsetMatrix);
        for (unsigned int j = 0; j < bone->mNumWeights; ++j) {
            imported.weights.emplace_back(static_cast<int>(bone->mWeights[j].mVertexId),
                                          bone->mWeights[j].mWeight);
        }
        result.boneMapping[imported.name] = static_cast<int>(result.bones.size());
        result.bones.push_back(std::move(imported));
    }
    result.gpu.setup(result.vertices, result.indices);
    return result;
}

/** @brief Extrai cor e textura albedo de um material. */
ImportedMaterial convertMaterial(const aiMaterial* material, const std::filesystem::path& modelDir) {
    ImportedMaterial result;
    aiColor3D color(1.0f, 1.0f, 1.0f);
    if (material->Get(AI_MATKEY_COLOR_DIFFUSE, color) == AI_SUCCESS) {
        result.color = glm::vec3(color.r, color.g, color.b);
    }
    aiString texturePath;
    if (material->GetTexture(aiTextureType_DIFFUSE, 0, &texturePath) == AI_SUCCESS) {
        std::filesystem::path tex(texturePath.C_Str());
        if (tex.is_relative()) {
            tex = modelDir / tex;
        }
        result.albedoTexture = tex.lexically_normal().string();
    }
    return result;
}

/** @brief Extrai clipes de animação. */
ImportedAnimation convertAnimation(const aiAnimation* animation) {
    ImportedAnimation result;
    result.name = animation->mName.C_Str();
    result.durationTicks = static_cast<float>(animation->mDuration);
    result.ticksPerSecond = animation->mTicksPerSecond > 0.0
                                ? static_cast<float>(animation->mTicksPerSecond)
                                : 25.0f;
    for (unsigned int i = 0; i < animation->mNumChannels; ++i) {
        const aiNodeAnim* channel = animation->mChannels[i];
        AnimationChannel converted;
        converted.nodeName = channel->mNodeName.C_Str();
        for (unsigned int j = 0; j < channel->mNumPositionKeys; ++j) {
            converted.positions.push_back({static_cast<float>(channel->mPositionKeys[j].mTime),
                                           toGlm(channel->mPositionKeys[j].mValue)});
        }
        for (unsigned int j = 0; j < channel->mNumRotationKeys; ++j) {
            converted.rotations.push_back({static_cast<float>(channel->mRotationKeys[j].mTime),
                                           toGlm(channel->mRotationKeys[j].mValue)});
        }
        for (unsigned int j = 0; j < channel->mNumScalingKeys; ++j) {
            converted.scales.push_back({static_cast<float>(channel->mScalingKeys[j].mTime),
                                        toGlm(channel->mScalingKeys[j].mValue)});
        }
        result.channels.push_back(std::move(converted));
    }
    return result;
}

/** @brief Interpola keys vec3. */
glm::vec3 sampleVec3(const std::vector<AnimKeyVec3>& keys, float time, const glm::vec3& fallback) {
    if (keys.empty()) {
        return fallback;
    }
    if (time <= keys.front().time) {
        return keys.front().value;
    }
    if (time >= keys.back().time) {
        return keys.back().value;
    }
    for (size_t i = 0; i + 1 < keys.size(); ++i) {
        if (time >= keys[i].time && time <= keys[i + 1].time) {
            const float span = keys[i + 1].time - keys[i].time;
            const float t = span > 0.0f ? (time - keys[i].time) / span : 0.0f;
            return glm::mix(keys[i].value, keys[i + 1].value, t);
        }
    }
    return keys.back().value;
}

/** @brief Interpola keys de rotação (slerp). */
glm::quat sampleQuat(const std::vector<AnimKeyQuat>& keys, float time, const glm::quat& fallback) {
    if (keys.empty()) {
        return fallback;
    }
    if (time <= keys.front().time) {
        return keys.front().value;
    }
    if (time >= keys.back().time) {
        return keys.back().value;
    }
    for (size_t i = 0; i + 1 < keys.size(); ++i) {
        if (time >= keys[i].time && time <= keys[i + 1].time) {
            const float span = keys[i + 1].time - keys[i].time;
            const float t = span > 0.0f ? (time - keys[i].time) / span : 0.0f;
            return glm::slerp(keys[i].value, keys[i + 1].value, t);
        }
    }
    return keys.back().value;
}

/** @brief Decompõe a transformação bind de um nó. */
void decomposeBind(const glm::mat4& transform, glm::vec3& pos, glm::quat& rot, glm::vec3& scale) {
    pos = glm::vec3(transform[3]);
    scale = glm::vec3(glm::length(glm::vec3(transform[0])),
                      glm::length(glm::vec3(transform[1])),
                      glm::length(glm::vec3(transform[2])));
    glm::mat3 rotMat(glm::vec3(transform[0]) / scale.x,
                     glm::vec3(transform[1]) / scale.y,
                     glm::vec3(transform[2]) / scale.z);
    rot = glm::quat_cast(rotMat);
}

/** @brief Calcula transforms animados de todos os nós. */
void computeNodeTransforms(const SceneNode& node, const glm::mat4& parent,
                           const std::unordered_map<std::string, const AnimationChannel*>& channels,
                           float time, std::unordered_map<std::string, glm::mat4>& out) {
    glm::mat4 local = node.transform;
    const auto it = channels.find(node.name);
    if (it != channels.end()) {
        const AnimationChannel* channel = it->second;
        glm::vec3 bindPos;
        glm::quat bindRot;
        glm::vec3 bindScale;
        decomposeBind(node.transform, bindPos, bindRot, bindScale);
        const glm::vec3 pos = sampleVec3(channel->positions, time, bindPos);
        const glm::quat rot = sampleQuat(channel->rotations, time, bindRot);
        const glm::vec3 scale = sampleVec3(channel->scales, time, bindScale);
        local = glm::translate(glm::mat4(1.0f), pos) * glm::mat4_cast(rot) *
                glm::scale(glm::mat4(1.0f), scale);
    }
    const glm::mat4 global = parent * local;
    out[node.name] = global;
    for (const auto& child : node.children) {
        computeNodeTransforms(child, global, channels, time, out);
    }
}

/** @brief Aplica skinning em CPU numa malha com ossos. */
void skinMesh(ImportedMesh& mesh, const std::unordered_map<std::string, glm::mat4>& nodeTransforms) {
    if (mesh.bones.empty()) {
        return;
    }
    std::vector<glm::mat4> boneMatrices(mesh.bones.size(), glm::mat4(1.0f));
    for (size_t i = 0; i < mesh.bones.size(); ++i) {
        const auto it = nodeTransforms.find(mesh.bones[i].name);
        if (it != nodeTransforms.end()) {
            boneMatrices[i] = it->second * mesh.bones[i].offset;
        }
    }
    std::vector<Vertex> skinned = mesh.vertices;
    std::vector<float> totalWeight(mesh.vertices.size(), 0.0f);
    std::vector<glm::vec3> accumPos(mesh.vertices.size(), glm::vec3(0.0f));
    std::vector<glm::vec3> accumNormal(mesh.vertices.size(), glm::vec3(0.0f));
    for (size_t b = 0; b < mesh.bones.size(); ++b) {
        const glm::mat3 normalMat(boneMatrices[b]);
        for (const auto& weight : mesh.bones[b].weights) {
            const int vertexIndex = weight.first;
            if (vertexIndex < 0 || vertexIndex >= static_cast<int>(mesh.vertices.size())) {
                continue;
            }
            accumPos[vertexIndex] += weight.second *
                                     glm::vec3(boneMatrices[b] * glm::vec4(mesh.vertices[vertexIndex].position, 1.0f));
            accumNormal[vertexIndex] += weight.second * (normalMat * mesh.vertices[vertexIndex].normal);
            totalWeight[vertexIndex] += weight.second;
        }
    }
    for (size_t i = 0; i < skinned.size(); ++i) {
        if (totalWeight[i] > 0.0f) {
            skinned[i].position = accumPos[i] / totalWeight[i];
            const glm::vec3 normal = accumNormal[i] / totalWeight[i];
            skinned[i].normal = glm::length(normal) > 0.0f ? glm::normalize(normal) : mesh.vertices[i].normal;
        }
    }
    mesh.gpu.updateVertices(skinned);
}

} // namespace

int ModelImporter::import(const std::string& path) {
    Assimp::Importer importer;
    const unsigned int flags = aiProcess_Triangulate | aiProcess_GenSmoothNormals |
                               aiProcess_JoinIdenticalVertices | aiProcess_FlipUVs |
                               aiProcess_LimitBoneWeights | aiProcess_ImproveCacheLocality;
    const aiScene* scene = nullptr;
    std::vector<uint8_t> bytes;
    if (std::filesystem::exists(path)) {
        scene = importer.ReadFile(path, flags);
    } else {
        bytes = FS::readBinary(path);
        if (!bytes.empty()) {
            const std::string hint = std::filesystem::path(path).extension().string();
            scene = importer.ReadFileFromMemory(bytes.data(), bytes.size(), flags, hint.c_str());
        }
    }
    if (!scene || !scene->mRootNode) {
        Log::error("ModelImporter: falha ao importar '" + path + "': " + importer.GetErrorString());
        return -1;
    }

    ImportedModel model;
    model.sourcePath = path;
    const std::filesystem::path modelDir = std::filesystem::path(path).parent_path();
    for (unsigned int i = 0; i < scene->mNumMaterials; ++i) {
        model.materials.push_back(convertMaterial(scene->mMaterials[i], modelDir));
    }
    for (unsigned int i = 0; i < scene->mNumMeshes; ++i) {
        model.meshes.push_back(convertMesh(scene->mMeshes[i]));
    }
    model.root = convertNode(scene->mRootNode);
    model.globalInverse = glm::inverse(toGlm(scene->mRootNode->mTransformation));
    for (unsigned int i = 0; i < scene->mNumAnimations; ++i) {
        model.animations.push_back(convertAnimation(scene->mAnimations[i]));
    }

    const int index = static_cast<int>(models_.size());
    models_.push_back(std::move(model));
    playback_.emplace_back();
    Log::info("ModelImporter: '" + path + "' importado (" +
              std::to_string(scene->mNumMeshes) + " malhas, " +
              std::to_string(scene->mNumAnimations) + " animações)");
    return index;
}

ImportedModel* ModelImporter::model(int index) {
    if (index < 0 || index >= static_cast<int>(models_.size())) {
        return nullptr;
    }
    return &models_[index];
}

int ModelImporter::modelCount() {
    return static_cast<int>(models_.size());
}

void ModelImporter::playAnimation(int modelIndex, int animationIndex, bool loop) {
    ImportedModel* m = model(modelIndex);
    if (!m || animationIndex < 0 || animationIndex >= static_cast<int>(m->animations.size())) {
        return;
    }
    PlaybackState& state = playback_[modelIndex];
    state.animationIndex = animationIndex;
    state.timeTicks = 0.0f;
    state.loop = loop;
    state.playing = true;
}

void ModelImporter::stopAnimation(int modelIndex) {
    ImportedModel* m = model(modelIndex);
    if (!m) {
        return;
    }
    playback_[modelIndex].playing = false;
    playback_[modelIndex].animationIndex = -1;
    for (auto& mesh : m->meshes) {
        if (!mesh.bones.empty()) {
            mesh.gpu.updateVertices(mesh.vertices);
        }
    }
}

void ModelImporter::update(float deltaTime) {
    for (size_t i = 0; i < models_.size(); ++i) {
        PlaybackState& state = playback_[i];
        if (!state.playing || state.animationIndex < 0) {
            continue;
        }
        ImportedModel& m = models_[i];
        const ImportedAnimation& anim = m.animations[state.animationIndex];
        state.timeTicks += deltaTime * anim.ticksPerSecond;
        if (state.timeTicks >= anim.durationTicks) {
            if (state.loop && anim.durationTicks > 0.0f) {
                state.timeTicks = std::fmod(state.timeTicks, anim.durationTicks);
            } else {
                state.timeTicks = anim.durationTicks;
                state.playing = false;
            }
        }
        std::unordered_map<std::string, const AnimationChannel*> channels;
        for (const auto& channel : anim.channels) {
            channels[channel.nodeName] = &channel;
        }
        std::unordered_map<std::string, glm::mat4> nodeTransforms;
        computeNodeTransforms(m.root, glm::mat4(1.0f), channels, state.timeTicks, nodeTransforms);
        for (auto& mesh : m.meshes) {
            skinMesh(mesh, nodeTransforms);
        }
    }
}

void ModelImporter::shutdown() {
    for (auto& m : models_) {
        for (auto& mesh : m.meshes) {
            mesh.gpu.destroy();
        }
    }
    models_.clear();
    playback_.clear();
}

} // namespace VelvX