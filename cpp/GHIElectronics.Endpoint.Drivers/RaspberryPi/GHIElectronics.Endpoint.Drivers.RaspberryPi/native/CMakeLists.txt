# libendpoint-zero2w.so: Pi Zero 2 W board driver, loadable by Python
# (ctypes.CDLL) and .NET (DllImport) so libendpoint's board-support dispatch
# sees the Pi as the active board. See libendpoint-zero2w.cpp for the full
# story. Built per arch:
#
#   cmake --preset endpoint-zero2w-arm
#   cmake --build --preset endpoint-zero2w-arm
#   cmake --preset endpoint-zero2w-arm64
#   cmake --build --preset endpoint-zero2w-arm64
#
# Output (both arches):
#   build/linux-arm/libendpoint-zero2w-arm.so
#   build/linux-arm64/libendpoint-zero2w-arm64.so
#
# publish_drvcpp.py stages these into the Pi Python wheel and .NET nupkg via
# the usual drivers release flow.

cmake_minimum_required(VERSION 3.19)

project(libendpoint_zero2w LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Hide everything except the two extern "C" entry points (the constructor
# runs via GCC attribute, not a looked-up symbol, but __attribute__((visibility
# ("default"))) on endpoint_zero2w_touch keeps it reachable from .NET's
# DllImport).
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# Include path: Zero2W.h lives one dir up (shipped as the C/C++ header-only
# option too), and it needs libendpoint's own headers (endpoint/BoardSupport.h,
# endpoint/Core.h) which live in Endpoint-Libraries/native/include.
set(LIBENDPOINT_NATIVE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../../../Endpoint-Libraries/native"
    CACHE PATH "Path to Endpoint-Libraries/native (for libendpoint headers)")

if(NOT EXISTS "${LIBENDPOINT_NATIVE_DIR}/include/endpoint/BoardSupport.h")
    message(FATAL_ERROR "LIBENDPOINT_NATIVE_DIR='${LIBENDPOINT_NATIVE_DIR}' does not contain include/endpoint/BoardSupport.h; "
                        "check the Endpoint-Libraries checkout path")
endif()

# Arch suffix on the output name so Python's loader can pick between
# libendpoint-zero2w-arm.so and libendpoint-zero2w-arm64.so by machine type
# without needing a separate package per arch. Matches the pattern Core uses
# for libendpoint-arm.so / libendpoint-arm64.so.
if(ENDPOINT_ARCH STREQUAL "aarch64")
    set(_arch_suffix "arm64")
elseif(ENDPOINT_ARCH STREQUAL "arm")
    set(_arch_suffix "arm")
else()
    message(FATAL_ERROR "ENDPOINT_ARCH must be 'arm' or 'aarch64'; got '${ENDPOINT_ARCH}'. "
                        "Pick a preset: cmake --preset endpoint-zero2w-arm[-64]")
endif()

add_library(endpoint-zero2w SHARED libendpoint-zero2w.cpp)
target_include_directories(endpoint-zero2w PRIVATE "${LIBENDPOINT_NATIVE_DIR}/include")

# -ldl for dlsym on glibc (static for musl; glibc needs it explicitly).
target_link_libraries(endpoint-zero2w PRIVATE dl)

# Final filename: libendpoint-zero2w-<arch>.so (no additional lib prefix, since
# CMake already adds "lib" for SHARED on Linux). OUTPUT_NAME strips the "lib"
# that CMake adds so we don't get "liblibendpoint-zero2w-arm.so". Actually
# CMake only adds ONE "lib" and we keep it, so OUTPUT_NAME is just the stem
# after "lib".
set_target_properties(endpoint-zero2w PROPERTIES
    OUTPUT_NAME "endpoint-zero2w-${_arch_suffix}"
    PREFIX "lib")

install(TARGETS endpoint-zero2w LIBRARY DESTINATION .)
