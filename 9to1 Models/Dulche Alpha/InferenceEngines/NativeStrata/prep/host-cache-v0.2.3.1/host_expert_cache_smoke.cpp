// AUTHORED ONLY. Root resource admission is required before compiling/running.
#include "../src/qwen35/thunder_host_expert_cache.h"
#include <cassert>
#include <cstdlib>
#include <cstring>
#include <vector>

using strata::qwen35::thunder::HostExpertCache;
using strata::qwen35::thunder::HostExpertCacheError;

int main() {
    std::size_t allocated = 0, freed = 0;
    const auto allocate = [&](std::size_t n) -> void* {
        allocated += n;
        return std::malloc(n);
    };
    const auto release = [&](void* p) { ++freed; std::free(p); };
    bool refused = false;
    try { HostExpertCache too_small(1, 32, allocate, release); }
    catch (const HostExpertCacheError&) { refused = true; }
    assert(refused && allocated == 0);

    HostExpertCache::Lease surviving;
    {
        HostExpertCache cache(16384, 32, allocate, release);
        assert(cache.charged_bytes() <= 16384 && cache.slot_count() > 1);
        int fills = 0;
        const auto fill = [&](std::uint8_t* p) { ++fills; std::memset(p, 0x71, 21); };
        auto first = cache.acquire({0, 7}, 21, fill);
        auto same = cache.acquire({0, 7}, 21, fill);
        assert(first.data() == same.data() && fills == 1 && first.size() == 21);
        for (std::size_t i = 0; i < 21; ++i) assert(first.data()[i] == 0x71);
        // Padding is zero and the cache never publishes a mutable pointer.
        assert(first.data()[21] == 0);

        std::vector<HostExpertCache::Lease> held;
        for (std::size_t i = 1; i < cache.slot_count(); ++i)
            held.push_back(cache.acquire({1, i}, 21, fill));
        refused = false;
        try { (void) cache.acquire({2, 0}, 21, fill); }
        catch (const HostExpertCacheError&) { refused = true; }
        assert(refused && first.data()[0] == 0x71);

        // A second borrower protects the original slot after one borrower leaves.
        first.reset();
        refused = false;
        try { (void) cache.acquire({2, 0}, 21, fill); }
        catch (const HostExpertCacheError&) { refused = true; }
        assert(refused);
        held.back().reset();
        auto replacement = cache.acquire({2, 0}, 9, [](std::uint8_t* p) { p[0] = 0x33; });
        assert(replacement.data()[0] == 0x33 && replacement.data()[1] == 0);
        assert(same.data()[0] == 0x71);

        // A failed fill leaves an unpublished reusable slot, not a partial hit.
        replacement.reset();
        refused = false;
        try {
            (void) cache.acquire({3, 0}, 9, [](std::uint8_t*) {
                throw HostExpertCacheError("fixture backing read failure");
            });
        } catch (const HostExpertCacheError&) { refused = true; }
        assert(refused);
        auto after_failure = cache.acquire({3, 0}, 9, [](std::uint8_t* p) { p[0] = 0x44; });
        assert(after_failure.data()[0] == 0x44 && same.data()[0] == 0x71);
        surviving = std::move(same);
    }
    // Outstanding leases retain the slab even when the cache handle is destroyed.
    assert(freed == 0 && surviving.data()[0] == 0x71);
    surviving.reset();
    assert(freed == 1);

    // Allocation refusal is typed and never yields an unusable cache.
    refused = false;
    try {
        HostExpertCache denied(16384, 32, [](std::size_t) -> void* { return nullptr; }, release);
    } catch (const HostExpertCacheError&) { refused = true; }
    assert(refused && freed == 1);
    return 0;
}
