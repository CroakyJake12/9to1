#pragma once
#include <stddef.h>
#include <stdint.h>
#ifdef _WIN32
#define NTO_LOK_EXPORT __declspec(dllexport)
#else
#define NTO_LOK_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct nto_lok_session nto_lok_session;
typedef void (*nto_lok_callback)(int type, const char* utf8_payload, void* context);
enum nto_lok_status {
    NTO_LOK_OK = 0, NTO_LOK_INVALID_ARGUMENT = 1, NTO_LOK_WRONG_THREAD = 2,
    NTO_LOK_RUNTIME_UNAVAILABLE = 3, NTO_LOK_VERSION_MISMATCH = 4,
    NTO_LOK_CAPABILITY_UNAVAILABLE = 5, NTO_LOK_DONOR_ERROR = 6,
    NTO_LOK_REENTRANT_CALL = 7
};
/* ABI 1 is a process-local adapter, not a replacement for the existing service API.
   All calls except error/free require the creating thread. Callback strings are
   borrowed only for the callback duration; copy before posting to a UI dispatcher.
   A successful command submission is not confirmation of command completion. */
NTO_LOK_EXPORT unsigned nto_lok_abi_version(void);
NTO_LOK_EXPORT const char* nto_lok_donor_commit(void);
NTO_LOK_EXPORT const char* nto_lok_last_error(void);
NTO_LOK_EXPORT int nto_lok_create(const char* program_directory, const char* profile_file_url,
    nto_lok_callback callback, void* callback_context, nto_lok_session** result);
NTO_LOK_EXPORT int nto_lok_destroy(nto_lok_session* session);
NTO_LOK_EXPORT int nto_lok_open(nto_lok_session* session, const char* brokered_file_url);
NTO_LOK_EXPORT int nto_lok_close_document(nto_lok_session* session);
NTO_LOK_EXPORT int nto_lok_document_size(nto_lok_session* session, int64_t* width_twips, int64_t* height_twips, int* tile_mode);
NTO_LOK_EXPORT int nto_lok_paint_tile(nto_lok_session* session, unsigned char* pixels, size_t capacity,
    int pixel_width, int pixel_height, int x_twips, int y_twips, int width_twips, int height_twips);
NTO_LOK_EXPORT int nto_lok_key(nto_lok_session* session, int type, int unicode_scalar, int donor_key_code);
NTO_LOK_EXPORT int nto_lok_pointer(nto_lok_session* session, int type, int x_twips, int y_twips,
    int click_count, int buttons, int modifiers);
NTO_LOK_EXPORT int nto_lok_composition(nto_lok_session* session, int type, const char* utf8_text);
NTO_LOK_EXPORT int nto_lok_selection(nto_lok_session* session, const char* mime_type, char** data, char** actual_mime);
NTO_LOK_EXPORT int nto_lok_paste(nto_lok_session* session, const char* mime_type, const char* data, size_t length);
NTO_LOK_EXPORT int nto_lok_command(nto_lok_session* session, const char* uno_command, const char* json_arguments);
NTO_LOK_EXPORT int nto_lok_save(nto_lok_session* session, const char* brokered_file_url, const char* format, const char* filter_options);
NTO_LOK_EXPORT void nto_lok_free(void* bridge_allocated_memory);
#ifdef __cplusplus
}
#endif
