#version 330 core
// VelvX Engine — fragmento 3D: luz direcional, textura albedo e sombra PCF.
in vec3 vFragPos;
in vec3 vNormal;
in vec2 vUV;
in vec4 vFragPosLightSpace;

uniform vec3 color;
uniform int useTexture;
uniform sampler2D albedo;
uniform vec3 lightDir;
uniform vec3 viewPos;
uniform float unlit;
uniform int shadowsEnabled;
uniform sampler2D shadowMap;

out vec4 FragColor;

float shadowFactor(vec4 fragPosLightSpace, vec3 normal, vec3 lightDirection) {
    vec3 proj = fragPosLightSpace.xyz / fragPosLightSpace.w;
    proj = proj * 0.5 + 0.5;
    if (proj.z > 1.0) {
        return 0.0;
    }
    float bias = max(0.0025 * (1.0 - dot(normal, lightDirection)), 0.0008);
    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0));
    for (int x = -1; x <= 1; ++x) {
        for (int y = -1; y <= 1; ++y) {
            float pcf = texture(shadowMap, proj.xy + vec2(x, y) * texel).r;
            shadow += proj.z - bias > pcf ? 1.0 : 0.0;
        }
    }
    return shadow / 9.0;
}

void main() {
    vec4 base = vec4(color, 1.0);
    if (useTexture == 1) {
        base *= texture(albedo, vUV);
    }
    if (unlit > 0.5) {
        FragColor = base;
        return;
    }
    vec3 n = normalize(vNormal);
    vec3 l = normalize(-lightDir);
    float diff = max(dot(n, l), 0.0);
    float shadow = 0.0;
    if (shadowsEnabled == 1) {
        shadow = shadowFactor(vFragPosLightSpace, n, l);
    }
    vec3 ambient = 0.28 * base.rgb;
    vec3 diffuse = (1.0 - shadow) * diff * 0.85 * base.rgb;
    vec3 viewDir = normalize(viewPos - vFragPos);
    vec3 halfDir = normalize(l + viewDir);
    float spec = pow(max(dot(n, halfDir), 0.0), 32.0) * 0.15 * (1.0 - shadow);
    FragColor = vec4(ambient + diffuse + vec3(spec), base.a);
}