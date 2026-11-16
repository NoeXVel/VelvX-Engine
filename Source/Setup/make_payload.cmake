# Gera Source/Setup/payload.zip (conteudo portable) a partir de Build Release.
# Executado via: cmake -P make_payload.cmake
set(ROOT "${CMAKE_CURRENT_LIST_DIR}/../..")
set(SRC "${ROOT}/Build Release")
set(STAGE "${CMAKE_CURRENT_LIST_DIR}/payload_stage")
set(OUT "${CMAKE_CURRENT_LIST_DIR}/payload.zip")

if(NOT EXISTS "${SRC}/VelvXLauncher.exe")
    message(FATAL_ERROR "Build Release ausente; compile a engine primeiro.")
endif()

file(REMOVE_RECURSE "${STAGE}")
file(MAKE_DIRECTORY "${STAGE}")
file(GLOB_RECURSE FILES RELATIVE "${SRC}" "${SRC}/*")
foreach(f IN LISTS FILES)
    if(IS_DIRECTORY "${SRC}/${f}")
        continue()
    endif()
    if(f MATCHES "\\.(pdb|log|ini|bak|stale|exp|ilk|obj)$")
        continue()
    endif()
    if(f MATCHES "(^|/)VelvXSetup\\.exe$")
        continue()
    endif()
    configure_file("${SRC}/${f}" "${STAGE}/${f}" COPYONLY)
endforeach()
configure_file("${ROOT}/packaging/Icon.ico" "${STAGE}/Icon.ico" COPYONLY)

if(EXISTS "${OUT}")
    file(REMOVE "${OUT}")
endif()
execute_process(
    COMMAND powershell -NoProfile -Command
        "Compress-Archive -Path '${STAGE}/*' -DestinationPath '${OUT}' -CompressionLevel Optimal"
    RESULT_VARIABLE rc)
file(REMOVE_RECURSE "${STAGE}")
if(NOT rc EQUAL 0)
    message(FATAL_ERROR "Compress-Archive falhou (rc=${rc}).")
endif()
message(STATUS "payload.zip gerado: ${OUT}")