# ============================================================================
#  VelvX Engine — CMake raiz
#
#  Alvos:
#    VelvXEngineCore  (DLL compartilhada: núcleo da engine)
#    VelvXPlayer      (PlayWindow: executa projetos sem o Editor)
#    VelvXEditor      (Editor ImGui docking)
#    VelvXLauncher    (Gerenciador de projetos)
#    VelvXScriptIDE   (IDE da Velv Language)
#    VelvXUIEditor    (Editor visual de UI)
#    CustomPhysics    (Plugin de exemplo)
#
#  Tudo é entregue em "../Build Release" (exes + DLL + shaders + runtime).
# ============================================================================
cmake_minimum_required(VERSION 3.20)
project(VelvXEngine VERSION 1.0.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

# Fontes UTF-8 (comentários e strings em português) em todos os alvos.
# /utf-8 não é usado: conflita com /source-charset:utf-8 do Assimp (D8016).
if(MSVC)
    add_compile_options(/source-charset:utf-8 /execution-charset:utf-8)
endif()

# ---------------------------------------------------------------------------
# Saída: todos os binários em "../Build Release"
# ---------------------------------------------------------------------------
set(VELVX_RELEASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../Build Release")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${VELVX_RELEASE_DIR}")
foreach(cfg IN ITEMS DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${cfg} "${VELVX_RELEASE_DIR}")
endforeach()

# ---------------------------------------------------------------------------
# Dependências vendorizadas (third_party/)
# ---------------------------------------------------------------------------
set(GLFW_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(GLFW_INSTALL OFF CACHE BOOL "" FORCE)
# GLFW e glad como DLLs: estado único de contexto/GL compartilhado entre
# VelvXEngineCore.dll, os executáveis e os plugins.
set(GLFW_LIBRARY_TYPE SHARED CACHE STRING "" FORCE)
add_subdirectory(third_party/glfw-3.4)

add_library(glad SHARED third_party/glad/src/gl.c)
target_include_directories(glad PUBLIC third_party/glad/include)
# dllexport no build do glad, dllimport nos consumidores (ponteiros GL são dados).
target_compile_definitions(glad PUBLIC GLAD_API_CALL_EXPORT PRIVATE GLAD_API_CALL_EXPORT_BUILD)

add_library(glm_headers INTERFACE)
target_include_directories(glm_headers INTERFACE third_party)

add_library(stb_headers INTERFACE)
target_include_directories(stb_headers INTERFACE third_party/stb)

add_library(miniz STATIC third_party/miniz/miniz.c)
target_include_directories(miniz PUBLIC third_party/miniz)

# --- Assimp: somente importadores FBX/glTF/OBJ, sem testes/ferramentas ---
set(ASSIMP_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ASSIMP_TOOLS OFF CACHE BOOL "" FORCE)
set(ASSIMP_INSTALL OFF CACHE BOOL "" FORCE)
set(ASSIMP_WARNINGS_AS_ERRORS OFF CACHE BOOL "" FORCE)
set(ASSIMP_IGNORE_GIT_HASH ON CACHE BOOL "" FORCE)
set(ASSIMP_NO_EXPORT ON CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ALL_IMPORTERS_BY_DEFAULT OFF CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ALL_EXPORTERS_BY_DEFAULT OFF CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_FBX_IMPORTER ON CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_GLTF_IMPORTER ON CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_OBJ_IMPORTER ON CACHE BOOL "" FORCE)
set(ASSIMP_BUILD_ZLIB ON CACHE BOOL "" FORCE)
add_subdirectory(third_party/assimp-5.4.3)

# --- ImGui (docking) com backend GLFW + OpenGL3 via GLAD ---
add_library(imgui STATIC
    third_party/imgui-docking/imgui.cpp
    third_party/imgui-docking/imgui_draw.cpp
    third_party/imgui-docking/imgui_tables.cpp
    third_party/imgui-docking/imgui_widgets.cpp
    third_party/imgui-docking/backends/imgui_impl_glfw.cpp
    third_party/imgui-docking/misc/cpp/imgui_stdlib.cpp
    cmake/imgui_impl_opengl3_glad.cpp
)
target_include_directories(imgui PUBLIC
    third_party/imgui-docking
    third_party/imgui-docking/backends
)
target_link_libraries(imgui PUBLIC glfw glad)
target_compile_definitions(imgui PRIVATE GLFW_INCLUDE_NONE IMGUI_IMPL_OPENGL_LOADER_CUSTOM)

# ---------------------------------------------------------------------------
# Configuração comum dos alvos VelvX (warnings + definições)
# ---------------------------------------------------------------------------
function(velvx_configure target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive-)
        # PDBs disponíveis para a opção "símbolos de debug" da exportação.
        target_compile_options(${target} PRIVATE /Zi)
        target_link_options(${target} PRIVATE /DEBUG /OPT:REF /OPT:ICF)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -pedantic -g)
    endif()
    target_compile_definitions(${target} PRIVATE GLFW_INCLUDE_NONE NOMINMAX)
endfunction()

# ---------------------------------------------------------------------------
# VelvXEngineCore — DLL compartilhada (estado global único para exe + plugins)
# ---------------------------------------------------------------------------
file(GLOB_RECURSE VELVX_CORE_SOURCES CONFIGURE_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/EngineCore/src/*.cpp)

add_library(VelvXEngineCore SHARED ${VELVX_CORE_SOURCES})
set_target_properties(VelvXEngineCore PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
target_include_directories(VelvXEngineCore PUBLIC EngineCore/include)
target_link_libraries(VelvXEngineCore
    PUBLIC glfw glad glm_headers stb_headers
    PRIVATE assimp miniz
)
velvx_configure(VelvXEngineCore)

# ---------------------------------------------------------------------------
# VelvXPlayer — PlayWindow (runtime de projetos)
# ---------------------------------------------------------------------------
add_executable(VelvXPlayer EngineCore/main.cpp)
target_link_libraries(VelvXPlayer PRIVATE VelvXEngineCore)
velvx_configure(VelvXPlayer)

# Conteúdo runtime entregue junto ao Player.
add_custom_command(TARGET VelvXPlayer POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/EngineCore/shaders" "${VELVX_RELEASE_DIR}/shaders"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/EngineCore/runtime/scripts" "${VELVX_RELEASE_DIR}/scripts"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/EngineCore/runtime/scenes" "${VELVX_RELEASE_DIR}/scenes"
    COMMENT "VelvX: copiando shaders/scripts/scenes para Build Release"
)

# ---------------------------------------------------------------------------
# Plugin de exemplo: CustomPhysics (DLL em Build Release/plugins)
# ---------------------------------------------------------------------------
file(GLOB CUSTOM_PHYSICS_SOURCES CONFIGURE_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/Plugins/CustomPhysics/*.cpp)
add_library(CustomPhysics SHARED ${CUSTOM_PHYSICS_SOURCES})
target_link_libraries(CustomPhysics PRIVATE VelvXEngineCore)
velvx_configure(CustomPhysics)
set_target_properties(CustomPhysics PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${VELVX_RELEASE_DIR}/plugins")
foreach(cfg IN ITEMS DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(CustomPhysics PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY_${cfg} "${VELVX_RELEASE_DIR}/plugins")
endforeach()

# ---------------------------------------------------------------------------
# Ferramentas (Editor / Launcher / ScriptIDE / UIEditor)
# Cada alvo é criado quando suas fontes existem em <Ferramenta>/src.
# ---------------------------------------------------------------------------
function(velvx_tool name subdir)
    file(GLOB TOOL_SOURCES CONFIGURE_DEPENDS
        ${CMAKE_CURRENT_SOURCE_DIR}/${subdir}/src/*.cpp)
    if(NOT TOOL_SOURCES)
        return()
    endif()
    add_executable(${name} ${TOOL_SOURCES})
    target_include_directories(${name} PRIVATE ${subdir}/include)
    target_link_libraries(${name} PRIVATE VelvXEngineCore imgui)
    velvx_configure(${name})
endfunction()

velvx_tool(VelvXEditor Editor)
velvx_tool(VelvXLauncher Launcher)
velvx_tool(VelvXScriptIDE ScriptIDE)
velvx_tool(VelvXUIEditor UIEditor)

# Editor de codigo Velv de painel unico, compartilhado por Editor e ScriptIDE.
foreach(tool VelvXEditor VelvXScriptIDE)
    if(TARGET ${tool})
        target_sources(${tool} PRIVATE CodeEditor/CodeEditor.cpp)
        target_include_directories(${tool} PRIVATE CodeEditor)
    endif()
endforeach()

# ---------------------------------------------------------------------------
# VelvXTests — bateria headless (interpretador / núcleo), sem janela/GL
# ---------------------------------------------------------------------------
if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/Tests/interpreter_tests.cpp)
    add_executable(VelvXTests Tests/interpreter_tests.cpp)
    target_link_libraries(VelvXTests PRIVATE VelvXEngineCore)
    velvx_configure(VelvXTests)
endif()

# ---------------------------------------------------------------------------
# VelvXSetup — instalador grafico nativo (arquivo unico, payload embutido).
# O payload.zip (conteudo portable) e gerado apos os binarios e embutido via RC.
# ---------------------------------------------------------------------------
set(VELVX_PAYLOAD "${CMAKE_CURRENT_SOURCE_DIR}/Setup/payload.zip")
# Sempre regenerado: o payload precisa refletir o Build Release atual.
add_custom_target(VelvXPayload ALL
    COMMAND ${CMAKE_COMMAND} -P "${CMAKE_CURRENT_SOURCE_DIR}/Setup/make_payload.cmake"
    COMMENT "VelvX: gerando payload.zip (portable) para o instalador")
add_dependencies(VelvXPayload VelvXPlayer VelvXEditor VelvXLauncher
                 VelvXScriptIDE VelvXUIEditor VelvXTests CustomPhysics VelvXContent)
if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/Setup/Setup.cpp)
    add_executable(VelvXSetup WIN32 Setup/Setup.cpp Setup/Setup.rc)
    add_dependencies(VelvXSetup VelvXPayload)
    target_include_directories(VelvXSetup PRIVATE Setup)
    target_link_libraries(VelvXSetup PRIVATE miniz)
    target_compile_definitions(VelvXSetup PRIVATE UNICODE _UNICODE)
    velvx_configure(VelvXSetup)
endif()

# ---------------------------------------------------------------------------
# Demos e templates entregues junto ao build
# ---------------------------------------------------------------------------
add_custom_target(VelvXContent ALL
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/Demos" "${VELVX_RELEASE_DIR}/Demos"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/templates" "${VELVX_RELEASE_DIR}/templates"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${CMAKE_CURRENT_SOURCE_DIR}/../Icon.png" "${VELVX_RELEASE_DIR}/Icon.png"
    COMMENT "VelvX: copiando Demos/templates/Icon para Build Release"
)