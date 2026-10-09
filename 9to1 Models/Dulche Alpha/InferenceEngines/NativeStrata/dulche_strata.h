#pragma once
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
/* Dulche-owned ABI version 1. Native sessions never own TaskRun or tool permission. */
typedef struct dulche_strata_session dulche_strata_session;
typedef struct dulche_strata_result dulche_strata_result;
typedef struct { const char* data; size_t size; } dulche_strata_text;
typedef struct { uint32_t kind; dulche_strata_text data; dulche_strata_text mime; } dulche_strata_part;
typedef struct { uint32_t role; dulche_strata_text text; dulche_strata_text name;
    const dulche_strata_part* parts; size_t part_count; } dulche_strata_message;
typedef struct { uint32_t maximum_new_tokens; double temperature; double top_p; uint32_t top_k;
    uint64_t seed; const dulche_strata_text* stops; size_t stop_count; dulche_strata_text reasoning; } dulche_strata_options;
typedef int (*dulche_strata_token_callback)(uint32_t token, const char* utf8, size_t size, void* state);
uint32_t dulche_strata_abi_version(void);
const char* dulche_strata_origin_commit(void);
int dulche_strata_has_cuda(void);
int dulche_strata_has_nccl(void);
const char* dulche_strata_registered_models(void);
dulche_strata_session* dulche_strata_create(void);
/* Every result owns its exact error/metric payload until free. Never throw C++ across the ABI. */
dulche_strata_result* dulche_strata_load(dulche_strata_session*, dulche_strata_text directory,
    dulche_strata_text registration, uint32_t context, const int* devices, size_t device_count);
dulche_strata_result* dulche_strata_generate(dulche_strata_session*, const dulche_strata_message*, size_t,
    const dulche_strata_options*, dulche_strata_token_callback, void*);
int dulche_strata_result_code(const dulche_strata_result*); /* 0 success, 1 validation, 2 OOM, 3 native error */
size_t dulche_strata_error_count(const dulche_strata_result*);
dulche_strata_text dulche_strata_error(const dulche_strata_result*, size_t);
dulche_strata_text dulche_strata_result_text(const dulche_strata_result*);
uint64_t dulche_strata_prompt_tokens(const dulche_strata_result*);
uint64_t dulche_strata_prefill_tokens(const dulche_strata_result*);
uint64_t dulche_strata_decode_tokens(const dulche_strata_result*);
double dulche_strata_prefill_seconds(const dulche_strata_result*);
double dulche_strata_decode_seconds(const dulche_strata_result*);
int dulche_strata_reused_tokens(const dulche_strata_result*, uint64_t*); /* 0 unavailable, 1 measured */
int dulche_strata_incremental_kv(const dulche_strata_result*, int*); /* 0 unavailable, 1 supported observation */
int dulche_strata_stopped(const dulche_strata_result*);
void dulche_strata_free_result(dulche_strata_result*);
void dulche_strata_destroy(dulche_strata_session*);
#ifdef __cplusplus
}
#endif
