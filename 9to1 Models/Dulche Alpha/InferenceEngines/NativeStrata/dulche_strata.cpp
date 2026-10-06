#include "dulche_strata.h"
#include "strata/app/runtime.hpp"
#include <exception>
#include <new>
#include <stdexcept>
#include <string>
#include <vector>

struct dulche_strata_session { strata::RuntimeSession runtime; };
struct dulche_strata_result { int code{}; strata::GenerationResult value; };
namespace {
std::string text(dulche_strata_text value) {
    if (value.size > strata::maximum_chat_request_bytes || (value.size != 0 && value.data == nullptr))
        throw std::invalid_argument("Invalid bounded UTF-8 span.");
    return value.size == 0 ? std::string{} : std::string(value.data, value.size);
}
dulche_strata_text view(const std::string& value) { return {value.data(), value.size()}; }
template<class F> dulche_strata_result* result(F&& body) noexcept {
    dulche_strata_result* output = nullptr;
    try { output = new dulche_strata_result; body(*output); }
    catch (const std::bad_alloc&) {
        if (output != nullptr) { output->code = 2; try { output->value.errors.emplace_back("Native out of memory."); } catch (...) {} }
    }
    catch (const std::exception& error) {
        if (output != nullptr) { output->code = 3; try { output->value.errors.emplace_back(error.what()); } catch (...) {} }
    }
    catch (...) {
        if (output != nullptr) { output->code = 3; try { output->value.errors.emplace_back("Unknown native failure."); } catch (...) {} }
    }
    return output;
}
}
extern "C" {
uint32_t dulche_strata_abi_version(void) { return 1; }
const char* dulche_strata_origin_commit(void) { return "015b075079c51a7aec670ee24924f920f5e7bb2b"; }
int dulche_strata_has_cuda(void) { return STRATA_HAS_CUDA; }
int dulche_strata_has_nccl(void) {
#ifdef STRATA_HAS_NCCL
    return STRATA_HAS_NCCL;
#else
    return 0;
#endif
}
const char* dulche_strata_registered_models(void) {
    try { static const std::string names = strata::registered_model_names(); return names.c_str(); } catch (...) { return nullptr; }
}
dulche_strata_session* dulche_strata_create(void) { try { return new dulche_strata_session; } catch (...) { return nullptr; } }
dulche_strata_result* dulche_strata_load(dulche_strata_session* session, dulche_strata_text directory,
    dulche_strata_text registration, uint32_t context, const int* devices, size_t device_count) {
    return result([&](dulche_strata_result& out) {
        if (session == nullptr || context == 0 || device_count > 64 || (device_count != 0 && devices == nullptr))
            throw std::invalid_argument("Invalid native session/context/device arguments.");
        const auto* model = strata::find_model_by_cli_name(text(registration));
        if (model == nullptr) { out.code = 1; out.value.errors.emplace_back("Unsupported exact native model registration."); return; }
        strata::RuntimeConfig config; config.model = model->model; config.maximum_context_tokens = context;
        if (device_count != 0) config.devices.assign(devices, devices + device_count);
        config.verbose = false; config.load_progress = false; config.use_placement_cache = false;
        config.enable_flash_attention = model->flash_attention_by_default;
        /* Research/topology/sampling optimizations are not enabled for benchmark rankings. */
        const auto loaded = session->runtime.initialize(text(directory), config);
        out.value.errors = loaded.errors; out.code = loaded.ok() ? 0 : 1;
    });
}
dulche_strata_result* dulche_strata_generate(dulche_strata_session* session, const dulche_strata_message* input,
    size_t count, const dulche_strata_options* input_options, dulche_strata_token_callback callback, void* state) {
    return result([&](dulche_strata_result& out) {
        if (session == nullptr || input_options == nullptr || count > 4096 || (count != 0 && input == nullptr)
            || input_options->stop_count > 128 || (input_options->stop_count != 0 && input_options->stops == nullptr))
            throw std::invalid_argument("Invalid bounded generation arguments.");
        std::vector<strata::ChatMessage> messages; messages.reserve(count);
        for (size_t index = 0; index < count; ++index) {
            const auto& raw = input[index];
            if (raw.role > 3 || raw.part_count > 128 || (raw.part_count != 0 && raw.parts == nullptr))
                throw std::invalid_argument("Unsupported role/content shape.");
            strata::ChatMessage message(static_cast<strata::ChatRole>(raw.role), text(raw.text), text(raw.name));
            for (size_t part_index = 0; part_index < raw.part_count; ++part_index) {
                const auto& part = raw.parts[part_index];
                if (part.kind > 1) throw std::invalid_argument("Unsupported multimodal input kind.");
                message.parts.push_back({static_cast<strata::ChatContentKind>(part.kind), text(part.data), text(part.mime)});
            }
            messages.push_back(std::move(message));
        }
        strata::GenerationOptions options; options.maximum_new_tokens = input_options->maximum_new_tokens;
        options.sampling.temperature = input_options->temperature; options.sampling.top_p = input_options->top_p;
        options.sampling.top_k = input_options->top_k; options.sampling.seed = input_options->seed;
        for (size_t index = 0; index < input_options->stop_count; ++index) options.stop.push_back(text(input_options->stops[index]));
        options.reasoning_effort = text(input_options->reasoning);
        std::string error;
        if (!strata::validate_chat_messages(messages, error) || !strata::validate_sampling_options(options.sampling, error)) {
            out.code = 1; out.value.errors.push_back(error); return;
        }
        out.value = session->runtime.generate_chat_stream(messages, options,
            [&](uint32_t token, std::string_view chunk) { return callback == nullptr || callback(token, chunk.data(), chunk.size(), state) != 0; });
        out.code = out.value.ok() ? 0 : 1;
    });
}
int dulche_strata_result_code(const dulche_strata_result* result) { return result == nullptr ? 2 : result->code; }
size_t dulche_strata_error_count(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.errors.size(); }
dulche_strata_text dulche_strata_error(const dulche_strata_result* result, size_t index) {
    return result != nullptr && index < result->value.errors.size() ? view(result->value.errors[index]) : dulche_strata_text{nullptr, 0};
}
dulche_strata_text dulche_strata_result_text(const dulche_strata_result* result) { return result == nullptr ? dulche_strata_text{nullptr,0} : view(result->value.text); }
uint64_t dulche_strata_prompt_tokens(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.metrics.prompt_tokens; }
uint64_t dulche_strata_prefill_tokens(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.metrics.prefill_tokens; }
uint64_t dulche_strata_decode_tokens(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.metrics.decode_tokens; }
double dulche_strata_prefill_seconds(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.metrics.prefill_seconds; }
double dulche_strata_decode_seconds(const dulche_strata_result* result) { return result == nullptr ? 0 : result->value.metrics.decode_seconds; }
int dulche_strata_reused_tokens(const dulche_strata_result* result, uint64_t* value) {
    if (result == nullptr || value == nullptr || !result->value.metrics.reused_prompt_tokens.has_value()) return 0;
    *value = *result->value.metrics.reused_prompt_tokens; return 1;
}
int dulche_strata_incremental_kv(const dulche_strata_result* result, int* value) {
    if (result == nullptr || value == nullptr || !result->value.metrics.incremental_kv_continuation.has_value()) return 0;
    *value = *result->value.metrics.incremental_kv_continuation ? 1 : 0; return 1;
}
int dulche_strata_stopped(const dulche_strata_result* result) { return result != nullptr && result->value.stopped ? 1 : 0; }
void dulche_strata_free_result(dulche_strata_result* result) { delete result; }
void dulche_strata_destroy(dulche_strata_session* session) { delete session; }
}
