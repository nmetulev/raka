cmake_minimum_required(VERSION 3.20)
project(RakaTap LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(raka_tap SHARED
    raka_tap.cpp
    raka_tap.def
)

target_compile_definitions(raka_tap PRIVATE
    UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX
)

# Link against Windows libraries
target_link_libraries(raka_tap PRIVATE
    psapi
)

# Output to artifacts directory
set_target_properties(raka_tap PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
