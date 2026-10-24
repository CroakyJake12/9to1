// Private prototype for Niko1221/Strata 011c9e600a93bbc9fd451c563501b5831c32bbca.
// Bounded packed-expert storage; the caller retains the immutable GGUF backing.
// No CUDA dependency: the engine supplies pinned allocation/deallocation callbacks.
#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <functional>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <stdexcept>
#include <utility>

namespace strata::qwen35::thunder {

class HostExpertCacheError : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};

class HostExpertCache {
public:
    struct Key {
        std::size_t layer = 0;
        std::size_t expert = 0;
        bool operator==(const Key& other) const noexcept {
            return layer == other.layer && expert == other.expert;
        }
    };
    using Allocate = std::function<void*(std::size_t)>;
    using Deallocate = std::function<void(void*)>;
    using Fill = std::function<void(std::uint8_t*)>;

private:
    struct Slot {
        Key key;
        std::size_t bytes = 0;
        std::size_t references = 0;
        std::uint64_t age = 0;
        bool occupied = false;
    };
    struct State {
        // Guard for allocator/control-block bookkeeping; CUDA driver allocations
        // and mapped-file/page-cache residency require separate reserve checks.
        static constexpr std::size_t bookkeeping_guard = 4096;
        const std::size_t slot_bytes;
        std::size_t slot_count = 0;
        std::size_t charged_bytes = 0;
        std::uint8_t* slab = nullptr;
        std::unique_ptr<Slot[]> slots;
        Deallocate deallocate;
        std::mutex mutex;
        std::uint64_t clock = 0;
        bool abandon_slab = false;

        State(std::size_t budget, std::size_t width, Allocate allocate, Deallocate release)
            : slot_bytes(width), deallocate(std::move(release)) {
            const std::size_t max = std::numeric_limits<std::size_t>::max();
            if (!width || width > max - sizeof(Slot) || !allocate || !deallocate)
                throw HostExpertCacheError("invalid host expert cache configuration");
            constexpr std::size_t overhead = sizeof(State) + bookkeeping_guard;
            if (budget <= overhead)
                throw HostExpertCacheError("host expert cache budget cannot admit metadata");
            slot_count = (budget - overhead) / (width + sizeof(Slot));
            if (!slot_count)
                throw HostExpertCacheError("host expert cache budget cannot admit one expert slot");
            charged_bytes = overhead + slot_count * (width + sizeof(Slot));
            // Multiplications cannot overflow: slot_count was divided from the budget.
            slots = std::make_unique<Slot[]>(slot_count);
            slab = static_cast<std::uint8_t*>(allocate(slot_count * width));
            if (!slab) throw HostExpertCacheError("host expert cache allocation refused");
        }
        ~State() noexcept {
            if (slab && !abandon_slab) {
                try { deallocate(slab); } catch (...) { /* deallocator contract is noexcept */ }
            }
        }
        std::uint64_t tick() noexcept {
            if (clock == std::numeric_limits<std::uint64_t>::max()) {
                for (std::size_t i = 0; i < slot_count; ++i) slots[i].age = 0;
                clock = 0;
            }
            return ++clock;
        }
        void release(std::size_t index) noexcept {
            std::lock_guard<std::mutex> guard(mutex);
            // Lease is move-only; each acquired reference has exactly one release.
            --slots[index].references;
        }
    };

public:
    class Lease {
        friend class HostExpertCache;
        std::shared_ptr<State> state_;
        std::size_t index_ = 0;
        Lease(std::shared_ptr<State> state, std::size_t index) noexcept
            : state_(std::move(state)), index_(index) {}

    public:
        Lease() noexcept = default;
        Lease(const Lease&) = delete;
        Lease& operator=(const Lease&) = delete;
        Lease(Lease&& other) noexcept
            : state_(std::move(other.state_)), index_(other.index_) {}
        Lease& operator=(Lease&& other) noexcept {
            if (this != &other) {
                reset();
                state_ = std::move(other.state_);
                index_ = other.index_;
            }
            return *this;
        }
        ~Lease() { reset(); }
        void reset() noexcept {
            if (state_) {
                state_->release(index_);
                state_.reset();
            }
        }
        const std::uint8_t* data() const noexcept {
            return state_ ? state_->slab + index_ * state_->slot_bytes : nullptr;
        }
        std::size_t size() const noexcept {
            return state_ ? state_->slots[index_].bytes : 0;
        }
        explicit operator bool() const noexcept { return static_cast<bool>(state_); }
    };

    HostExpertCache(std::size_t budget, std::size_t slot_bytes, Allocate allocate, Deallocate release) {
        try {
            state_ = std::make_shared<State>(budget, slot_bytes, std::move(allocate), std::move(release));
        } catch (const std::bad_alloc&) {
            throw HostExpertCacheError("host expert cache metadata allocation refused");
        }
    }
    HostExpertCache(const HostExpertCache&) = delete;
    HostExpertCache& operator=(const HostExpertCache&) = delete;

    // The fill callback copies exact packed bytes only. It must not recursively
    // acquire from this cache; publication occurs after successful completion.
    Lease acquire(Key key, std::size_t bytes, const Fill& fill) {
        State& s = *state_;
        std::lock_guard<std::mutex> guard(s.mutex);
        if (!bytes || bytes > s.slot_bytes || !fill)
            throw HostExpertCacheError("expert exceeds admitted host cache slot size");
        std::size_t victim = s.slot_count;
        for (std::size_t i = 0; i < s.slot_count; ++i) {
            Slot& slot = s.slots[i];
            if (slot.occupied && slot.key == key) {
                if (slot.bytes != bytes)
                    throw HostExpertCacheError("expert cache key reused with a different layout");
                if (slot.references == std::numeric_limits<std::size_t>::max())
                    throw HostExpertCacheError("expert cache lease count overflow");
                ++slot.references;
                slot.age = s.tick();
                return Lease(state_, i);
            }
            if (!slot.references &&
                (victim == s.slot_count || (!slot.occupied && s.slots[victim].occupied) ||
                 (slot.occupied == s.slots[victim].occupied && slot.age < s.slots[victim].age)))
                victim = i;
        }
        if (victim == s.slot_count)
            throw HostExpertCacheError("host expert cache exhausted: every slot has a live lease");
        Slot& slot = s.slots[victim];
        slot.occupied = false;
        std::uint8_t* dst = s.slab + victim * s.slot_bytes;
        // Include the stride padding/tail in the admitted immutable allocation.
        std::memset(dst, 0, s.slot_bytes);
        fill(dst);
        slot.key = key;
        slot.bytes = bytes;
        slot.references = 1;
        slot.age = s.tick();
        slot.occupied = true;
        return Lease(state_, victim);
    }

    std::size_t charged_bytes() const noexcept { return state_->charged_bytes; }
    std::size_t slot_count() const noexcept { return state_->slot_count; }
    std::size_t slot_bytes() const noexcept { return state_->slot_bytes; }

    // Only after a CUDA fence fails during final engine retirement: preserving the
    // allocation until process teardown is safer than freeing a possible DMA source.
    // This is not a retry path; the engine returns failure and cannot be reused.
    void abandon_allocation() noexcept {
        std::lock_guard<std::mutex> guard(state_->mutex);
        state_->abandon_slab = true;
    }

private:
    std::shared_ptr<State> state_;
};

} // namespace strata::qwen35::thunder
