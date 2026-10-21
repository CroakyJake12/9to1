// NativeStrataBuildEngineArgsFixture.cpp
//
// Focused native argv fixture for the PRODUCTION candidate engine-argument builder
// (`build_engine_args` in dulche_strata_candidate_worker.cpp). It exercises the real function —
// NOT a source-string mirror — by setting the held model-path env and asserting the exact argv the
// worker would spawn: `--model <held-gguf> --serve --gpu <comma-separated> [--max-context N]`,
// with NO literal "generate" subcommand and NO "--pack" (the upstream qwen35moe engine dispatch
// requires --model/--native on a real Qwen35Moe GGUF; a --pack folder alone cannot reach it).
// The approved device selection maps to the upstream `--gpu` comma-separated argv (the parser in
// generate.cpp:2356 sets CUDA_VISIBLE_DEVICES before CUDA init; q36_engine.cu:3753 consumes it).
//
// HONEST CAPABILITY LABEL: this fixture proves ARGV FORWARDING only. The upstream q36 engine source
// is a single-GPU code path; forwarding a comma-separated multi-device `--gpu` list does NOT verify
// or claim actual multi-GPU compute. No GPU/Ready is touched here.
//
// STATUS: AUTHORED / COMPILE+RUN NOT RUN in this source-only checkpoint (no Root/native compile
// allocation here). Compile under a Root native slot with the same include flags as the candidate:
//
//   g++ -std=c++20 -O1 -pthread -I <native> \
//       -DSTRATA_NATIVE_EXT="" -DSTRATA_HAS_CUDA=0 \
//       -o build-engine-args-fixture NativeStrataBuildEngineArgsFixture.cpp
//
// where <native> is the absolute path of InferenceEngines/NativeStrata. No engine/model/CUDA
// symbols are required.

#include <cstdlib>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

#ifndef STRATA_NATIVE_EXT
#define STRATA_NATIVE_EXT ""
#endif
#ifndef STRATA_HAS_CUDA
#define STRATA_HAS_CUDA 0
#endif

// Rename the candidate worker's main so this fixture supplies its own driver.
#define main dulche_strata_candidate_worker_main
#include "dulche_strata_candidate_worker.cpp"
#undef main

static int g_checks = 0;

static void expect_arg(const std::vector<std::string>& args, size_t i, const std::string& want) {
    if (!(i < args.size() && args[i] == want)) {
        std::cerr << "FAIL: arg[" << i << "] = '"
                  << (i < args.size() ? args[i] : "<missing>") << "' want '" << want << "'\n";
        std::exit(1);
    }
    ++g_checks;
}

template <typename Fn>
static void expect_throw(Fn&& fn, const char* what) {
    try {
        fn();
        std::cerr << "FAIL: expected throw on " << what << "\n";
        std::exit(1);
    } catch (const std::invalid_argument&) {
        ++g_checks;
    }
}

int main() {
    // Use a real, existing fd path so trusted_held_path existence checks pass.
    std::string held = "/proc/self/fd/0";  // stdin exists in this process
    setenv("STRATA_MODEL_PATH", held.c_str(), 1);

    // Single-device argv: --model <held> --serve --gpu 2 --max-context 2048
    {
        auto a = build_engine_args(2048, {2});
        if (!(a.size() == 7)) { std::cerr << "FAIL: single-device size\n"; return 1; }
        expect_arg(a, 0, "--model");
        expect_arg(a, 1, held);
        expect_arg(a, 2, "--serve");
        expect_arg(a, 3, "--gpu");
        expect_arg(a, 4, "2");
        expect_arg(a, 5, "--max-context");
        expect_arg(a, 6, "2048");
    }

    // Multi-device argv maps to a comma-separated --gpu value (argv forwarding only; not multi-GPU
    // compute proof).
    {
        auto a = build_engine_args(2048, {3, 0, 7});
        if (!(a.size() == 7)) { std::cerr << "FAIL: multi-device size\n"; return 1; }
        expect_arg(a, 3, "--gpu");
        expect_arg(a, 4, "3,0,7");
    }

    // Zero context omits --max-context; an explicit device is still required and forwarded.
    {
        auto a = build_engine_args(0, {2});
        if (!(a.size() == 5 && a[0] == "--model" && a[1] == held && a[2] == "--serve"
              && a[3] == "--gpu" && a[4] == "2")) {
            std::cerr << "FAIL: zero-context with device argv shape\n"; return 1;
        }
        ++g_checks;
    }

    // No literal "generate" or "--pack" ever appears.
    {
        auto a = build_engine_args(2048, {2});
        for (const auto& s : a) {
            if (s == "generate" || s == "--pack") {
                std::cerr << "FAIL: banned argv token '" << s << "'\n"; return 1;
            }
        }
    }

    // Fail closed on an EMPTY (ambiguous) selection — never silently omit --gpu.
    expect_throw([] { build_engine_args(2048, {}); }, "empty device selection");

    // Fail closed on a duplicate selection (ambiguous).
    expect_throw([] { build_engine_args(2048, {2, 2}); }, "duplicate device selection");
    expect_throw([] { build_engine_args(2048, {1, 5, 1}); }, "non-adjacent duplicate");

    // Fail closed on a device index beyond the int range (cannot be a valid CUDA index).
    {
        uint32_t huge = static_cast<uint32_t>(std::numeric_limits<int>::max()) + 1u;
        expect_throw([&] { build_engine_args(2048, {huge}); }, "out-of-range device index");
    }

    // Missing held model path fails closed (throws), never fabricates an engine launch.
    unsetenv("STRATA_MODEL_PATH");
    expect_throw([] { build_engine_args(1024, {0}); }, "missing model path");

    std::cout << "OK build_engine_args checks=" << g_checks << "\n";
    return 0;
}
