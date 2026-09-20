#ifndef CAKEOS_IMAGES_BACKEND_H
#define CAKEOS_IMAGES_BACKEND_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAKE_IMAGES_ABI_VERSION 1

typedef int32_t CakeImagesStatus;

#define CAKE_IMAGES_STATUS_OK 0
#define CAKE_IMAGES_STATUS_INVALID_HANDLE -1
#define CAKE_IMAGES_STATUS_INVALID_ARGUMENT -2
#define CAKE_IMAGES_STATUS_INVALID_STATE -3
#define CAKE_IMAGES_STATUS_DECODE_ERROR -4
#define CAKE_IMAGES_STATUS_RENDER_ERROR -5
#define CAKE_IMAGES_STATUS_METADATA_ERROR -6
#define CAKE_IMAGES_STATUS_COLOR_MANAGEMENT_ERROR -7
#define CAKE_IMAGES_STATUS_IO_ERROR -8
#define CAKE_IMAGES_STATUS_PANIC -9

typedef struct {
    int32_t width;
    int32_t height;
    int32_t has_alpha;
    char* color_space;
    char* format;
} CakeImagesInfo;

typedef struct {
    char* exif_json;
    char* xmp_json;
    char* iptc_json;
    int32_t width;
    int32_t height;
    int32_t orientation;
    char* color_profile;
    double dpi_x;
    double dpi_y;
} CakeImagesMetadata;

typedef struct {
    double x;
    double y;
    double scale;
    int32_t target_width;
    int32_t target_height;
} CakeImagesViewport;

typedef struct {
    void* cairo_surface;
    int32_t width;
    int32_t height;
    int32_t stride;
} CakeImagesRenderResult;

typedef struct {
    uint8_t* data;
    size_t len;
    int32_t width;
    int32_t height;
} CakeImagesThumbnailResult;

typedef enum {
    CAKE_IMAGES_TRANSFORM_ROTATE_90 = 0,
    CAKE_IMAGES_TRANSFORM_ROTATE_180 = 1,
    CAKE_IMAGES_TRANSFORM_ROTATE_270 = 2,
    CAKE_IMAGES_TRANSFORM_FLIP_HORIZONTAL = 3,
    CAKE_IMAGES_TRANSFORM_FLIP_VERTICAL = 4,
    CAKE_IMAGES_TRANSFORM_EXIF_AUTO = 5,
} CakeImagesTransformOp;

typedef struct {
    void* new_handle;
    int32_t lossless;
} CakeImagesTransformResult;

uint32_t cake_images_abi_version(void);
void* cake_images_decode(const char* path, CakeImagesInfo* out_info);
void cake_images_free(void* handle);
CakeImagesStatus cake_images_render(const void* handle, CakeImagesViewport viewport, CakeImagesRenderResult* out_result);
CakeImagesStatus cake_images_metadata_read(const void* handle, CakeImagesMetadata* out_metadata);
void cake_images_metadata_free(CakeImagesMetadata* metadata);
CakeImagesStatus cake_images_thumbnail(const char* path, int32_t max_px, CakeImagesThumbnailResult* out_result);
void cake_images_thumbnail_free(CakeImagesThumbnailResult* result);
CakeImagesStatus cake_images_transform_apply(void* handle, CakeImagesTransformOp op, CakeImagesTransformResult* out_result);
CakeImagesStatus cake_images_color_profile_apply(void* handle, const char* profile_path, int32_t intent);

#ifdef __cplusplus
}
#endif

#endif /* CAKEOS_IMAGES_BACKEND_H */