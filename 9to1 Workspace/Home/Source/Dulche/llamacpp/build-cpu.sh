#!/bin/sh
set -eu

EXPECTED_SHA='5266f24da75dc449bd56cbed7addb9c8e4a6a73e'
SOURCE=${LLAMA_SOURCE:-}
BUILD=${LLAMA_BUILD_DIR:-}
PARALLEL=${LLAMA_BUILD_PARALLEL:-2}

if [ -z "$SOURCE" ] || [ -z "$BUILD" ]; then
  echo 'Set LLAMA_SOURCE to an existing llama.cpp checkout and LLAMA_BUILD_DIR to an out-of-tree build directory.' >&2
  exit 64
fi
if [ ! -d "$SOURCE/.git" ]; then
  echo 'LLAMA_SOURCE is not a Git checkout.' >&2
  exit 65
fi
case "$PARALLEL" in
  ''|*[!0-9]*|0)
    echo 'LLAMA_BUILD_PARALLEL must be a positive integer.' >&2
    exit 67
    ;;
esac

ACTUAL_SHA=$(git -C "$SOURCE" rev-parse HEAD)
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
  echo "Refusing unpinned llama.cpp source: expected $EXPECTED_SHA, got $ACTUAL_SHA" >&2
  exit 66
fi

cmake -S "$SOURCE" -B "$BUILD" \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DLLAMA_BUILD_IS_DEV=OFF \
  -DLLAMA_BUILD_COMMON=ON \
  -DLLAMA_BUILD_TOOLS=ON \
  -DLLAMA_BUILD_SERVER=ON \
  -DLLAMA_BUILD_APP=OFF \
  -DLLAMA_BUILD_UI=OFF \
  -DLLAMA_USE_PREBUILT_UI=OFF \
  -DLLAMA_BUILD_EXAMPLES=OFF \
  -DLLAMA_BUILD_TESTS=OFF \
  -DLLAMA_TOOLS_INSTALL=OFF \
  -DLLAMA_TESTS_INSTALL=OFF \
  -DLLAMA_OPENSSL=OFF \
  -DLLAMA_SUBPROCESS=OFF \
  -DGGML_NATIVE=OFF \
  -DGGML_CCACHE=OFF \
  -DGGML_CPU=ON \
  -DGGML_RPC=OFF \
  -DGGML_CUDA=OFF \
  -DGGML_HIP=OFF \
  -DGGML_VULKAN=OFF \
  -DGGML_SYCL=OFF \
  -DGGML_OPENCL=OFF \
  -DGGML_OPENVINO=OFF

cmake --build "$BUILD" --target llama-server --parallel "$PARALLEL"
test -x "$BUILD/bin/llama-server"
"$BUILD/bin/llama-server" --version
