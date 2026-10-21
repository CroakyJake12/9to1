#!/usr/bin/env bash
set -euo pipefail
pin=5266f24da75dc449bd56cbed7addb9c8e4a6a73e
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
artifact=$PWD/engine-artifact
mkdir -p "$artifact/evidence" "$artifact/payload/bin" "$artifact/payload/licenses"
# Compiler output is kept even when configure/build fails; no failure becomes a PASS.
exec > >(tee "$artifact/evidence/build-and-smoke.log") 2>&1
uname -a > "$artifact/evidence/native-platform.txt"
cat /etc/os-release >> "$artifact/evidence/native-platform.txt"
getconf GNU_LIBC_VERSION >> "$artifact/evidence/native-platform.txt"
dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' > "$artifact/evidence/build-packages.tsv"
cmake --version
c++ --version
source_dir=$RUNNER_TEMP/llamacpp-source
build_dir=$RUNNER_TEMP/llamacpp-build
model_dir=$RUNNER_TEMP/pinned-model
if [[ -e "$source_dir" || -e "$build_dir" || -e "$model_dir" ]]; then
  echo 'Refuse pre-existing engine task trees'; exit 2
fi
mkdir -p "$source_dir" "$model_dir"
git -C "$source_dir" init -q
git -C "$source_dir" remote add origin https://github.com/ggml-org/llama.cpp.git
git -C "$source_dir" fetch --depth=1 origin "$pin"
git -C "$source_dir" checkout --detach FETCH_HEAD
test "$(git -C "$source_dir" rev-parse HEAD)" = "$pin"
test -z "$(git -C "$source_dir" status --porcelain)"
printf '%s\n' "$pin" > "$artifact/payload/SOURCE-COMMIT"
git -C "$source_dir" archive HEAD | gzip -n > "$artifact/llama.cpp-$pin-source.tar.gz"
cp "$source_dir/LICENSE" "$artifact/payload/licenses/llama.cpp-MIT.txt"
# Match existing project build-cpu.sh contract. CPU ISA and stdlib portability
# are explicit additions; glibc remains dynamic on the actually recorded base.
cmake -S "$source_dir" -B "$build_dir" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_EXPORT_COMPILE_COMMANDS=ON \
  -DCMAKE_EXE_LINKER_FLAGS='-static-libgcc -static-libstdc++' \
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
  -DLLAMA_LLGUIDANCE=OFF \
  -DLLAMA_BUILD_NUMBER=10809 \
  -DLLAMA_BUILD_COMMIT="$pin" \
  -DGGML_NATIVE=OFF \
  -DGGML_CPU_ARM_ARCH=armv8-a \
  -DGGML_CPU_ALL_VARIANTS=OFF \
  -DGGML_BACKEND_DL=OFF \
  -DGGML_CPU_KLEIDIAI=OFF \
  -DGGML_BLAS=OFF \
  -DGGML_LLAMAFILE=OFF \
  -DGGML_OPENMP=OFF \
  -DGGML_CCACHE=OFF \
  -DGGML_CPU=ON \
  -DGGML_RPC=OFF \
  -DGGML_CUDA=OFF \
  -DGGML_HIP=OFF \
  -DGGML_VULKAN=OFF \
  -DGGML_SYCL=OFF \
  -DGGML_OPENCL=OFF \
  -DGGML_OPENVINO=OFF
cmake --build "$build_dir" --target llama-server --parallel 2
test -x "$build_dir/bin/llama-server"
cp "$build_dir/bin/llama-server" "$artifact/payload/bin/llama-server"
cp "$build_dir/CMakeCache.txt" "$artifact/evidence/CMakeCache.txt"
cp "$build_dir/compile_commands.json" "$artifact/evidence/compile_commands.json"
for notice in /usr/share/doc/gcc-*-base/copyright; do
  [[ -f "$notice" ]] && cp "$notice" "$artifact/payload/licenses/$(basename "$(dirname "$notice")")-copyright.txt"
done
python3 "$script_dir/verify-engine-closure.py" "$artifact/payload/bin/llama-server" "$artifact/evidence/engine-closure.json"
"$artifact/payload/bin/llama-server" --version > "$artifact/evidence/server-version.txt" 2>&1
python3 "$script_dir/smoke-engine-http.py" "$artifact/payload/bin/llama-server" "$model_dir" "$artifact/evidence"
cp "$artifact/evidence/model-card.md" "$artifact/payload/licenses/Qwen3-GGUF-model-card.md"
cp "$artifact/evidence/Qwen3-Apache-2.0.txt" "$artifact/payload/licenses/Qwen3-Apache-2.0.txt"
cp "$artifact/evidence/model-source.json" "$artifact/payload/MODEL-SOURCE.json"
python3 - "$artifact" <<'PY'
import datetime,hashlib,json,os,pathlib,subprocess,sys
r=pathlib.Path(sys.argv[1]);payload=r/'payload';binary=payload/'bin/llama-server'
build={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'official_repository':'https://github.com/ggml-org/llama.cpp','source_commit':'5266f24da75dc449bd56cbed7addb9c8e4a6a73e','source_release':'b10809','project_pin_version':'0.4.0','rebuilt_binary_not_official_release_asset':True,'native_machine':os.uname().machine,'debian_base':os.environ['PREVIEW_DEBIAN_BASE'],'architecture':'linux-arm64','cpu_target':'armv8-a','internal_libraries':'static','CXX_GCC_runtimes':'static flags; verified DT_NEEDED closure','glibc':'dynamic, see actual engine-closure.json','UI_OpenSSL_subprocess_GPU':'OFF','server_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'application_acceptance':'NOT RUN','laptop_acceptance':'NOT RUN','performance_gain':'NOT CLAIMED'}
(payload/'BUILD-INFO.json').write_text(json.dumps(build,indent=2)+'\n')
(r/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(r))+'\n' for p in sorted(r.rglob('*')) if p.is_file() and p.name!='SHA256SUMS' and p.name!='build-and-smoke.log'))
PY
tar -C "$artifact/payload" -czf "$artifact/llamacpp-b10809-linux-arm64-${PREVIEW_DEBIAN_BASE}.tar.gz" .
sha256sum "$artifact/llamacpp-b10809-linux-arm64-${PREVIEW_DEBIAN_BASE}.tar.gz" > "$artifact/ENGINE-ARCHIVE.sha256"
