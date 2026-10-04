//! CakeOS Images Backend - glycin/libglycin integration
//!
//! This crate provides the native backend for CakeUI Images, wrapping glycin >= 2.0
//! for image decoding, format support, metadata extraction, color management,
//! and Cairo/Skia rendering.

use std::ffi::{c_void, CStr, CString};
use std::os::raw::{c_char, c_int, c_double, c_uint, c_ulong};
use std::ptr;
use std::slice;

use anyhow::{Context, Result};
use glycin::{Image, ImageInfo, Metadata, ColorProfile, RenderOptions};
use image::ImageFormat;
use lcms2::{Intent, Transform};

pub const CAKE_IMAGES_ABI_VERSION: u32 = 1;

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CakeImagesStatus {
    Ok = 0,
    InvalidHandle = -1,
    InvalidArgument = -2,
    InvalidState = -3,
    DecodeError = -4,
    RenderError = -5,
    MetadataError = -6,
    ColorManagementError = -7,
    IoError = -8,
    Panic = -9,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesInfo {
    pub width: c_int,
    pub height: c_int,
    pub has_alpha: c_int,
    pub color_space: *mut c_char,
    pub format: *mut c_char,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesMetadata {
    pub exif_json: *mut c_char,
    pub xmp_json: *mut c_char,
    pub iptc_json: *mut c_char,
    pub width: c_int,
    pub height: c_int,
    pub orientation: c_int,
    pub color_profile: *mut c_char,
    pub dpi_x: c_double,
    pub dpi_y: c_double,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesViewport {
    pub x: c_double,
    pub y: c_double,
    pub scale: c_double,
    pub target_width: c_int,
    pub target_height: c_int,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesRenderResult {
    pub cairo_surface: *mut c_void,
    pub width: c_int,
    pub height: c_int,
    pub stride: c_int,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesThumbnailResult {
    pub data: *mut u8,
    pub len: c_ulong,
    pub width: c_int,
    pub height: c_int,
}

#[repr(u32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CakeImagesTransformOp {
    Rotate90 = 0,
    Rotate180 = 1,
    Rotate270 = 2,
    FlipHorizontal = 3,
    FlipVertical = 4,
    ExifAuto = 5,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeImagesTransformResult {
    pub new_handle: *mut c_void,
    pub lossless: c_int,
}

struct ImageHandle {
    image: Image,
    path: String,
}

fn guard_status<F, T>(operation: F) -> Result<T, CakeImagesStatus>
where
    F: FnOnce() -> Result<T> + std::panic::UnwindSafe,
{
    std::panic::catch_unwind(operation)
        .unwrap_or(Err(anyhow::anyhow!("panic in backend")))
        .map_err(|e| {
            eprintln!("Backend error: {e:?}");
            CakeImagesStatus::Panic
        })
}

fn with_handle<T>(
    handle: *mut c_void,
    operation: impl FnOnce(&mut ImageHandle) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }
    let handle = unsafe { &mut *handle.cast::<ImageHandle>() };
    Some(operation(handle))
}

fn with_handle_const<T>(
    handle: *const c_void,
    operation: impl FnOnce(&ImageHandle) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }
    let handle = unsafe { &*handle.cast::<ImageHandle>() };
    Some(operation(handle))
}

fn c_string(s: String) -> *mut c_char {
    CString::new(s).unwrap_or_default().into_raw()
}

unsafe fn free_c_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        let _ = CString::from_raw(ptr);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_abi_version() -> u32 {
    CAKE_IMAGES_ABI_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_decode(
    path: *const c_char,
    out_info: *mut CakeImagesInfo,
) -> *mut c_void {
    guard_status(|| {
        if path.is_null() || out_info.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let path_str = unsafe { CStr::from_ptr(path) }.to_str()?;
        let image = Image::open(path_str).context("failed to decode image")?;
        let info = image.info();

        let handle = Box::into_raw(Box::new(ImageHandle {
            image,
            path: path_str.to_string(),
        }));

        unsafe {
            *out_info = CakeImagesInfo {
                width: info.width as c_int,
                height: info.height as c_int,
                has_alpha: if info.has_alpha { 1 } else { 0 },
                color_space: c_string(info.color_space.clone()),
                format: c_string(info.format.clone()),
            };
        }

        Ok(handle)
    }).unwrap_or(ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_free(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    let _ = guard_status(|| {
        unsafe {
            drop(Box::from_raw(handle.cast::<ImageHandle>()));
        }
        Ok(())
    });
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_render(
    handle: *const c_void,
    viewport: CakeImagesViewport,
    out_result: *mut CakeImagesRenderResult,
) -> CakeImagesStatus {
    guard_status(|| {
        if handle.is_null() || out_result.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let result = with_handle_const(handle, |h| {
            let options = RenderOptions {
                x: viewport.x,
                y: viewport.y,
                scale: viewport.scale,
                target_width: viewport.target_width as u32,
                target_height: viewport.target_height as u32,
            };
            h.image.render(options)
        }).ok_or_else(|| anyhow::anyhow!("invalid handle"))??;

        unsafe {
            *out_result = CakeImagesRenderResult {
                cairo_surface: result.cairo_surface,
                width: result.width as c_int,
                height: result.height as c_int,
                stride: result.stride as c_int,
            };
        }

        Ok(())
    }).unwrap_or(CakeImagesStatus::RenderError)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_metadata_read(
    handle: *const c_void,
    out_metadata: *mut CakeImagesMetadata,
) -> CakeImagesStatus {
    guard_status(|| {
        if handle.is_null() || out_metadata.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let metadata = with_handle_const(handle, |h| h.image.metadata().cloned())
            .ok_or_else(|| anyhow::anyhow!("invalid handle"))?
            .ok_or_else(|| anyhow::anyhow!("no metadata"))?;

        unsafe {
            *out_metadata = CakeImagesMetadata {
                exif_json: c_string(metadata.exif_json.unwrap_or_default()),
                xmp_json: c_string(metadata.xmp_json.unwrap_or_default()),
                iptc_json: c_string(metadata.iptc_json.unwrap_or_default()),
                width: metadata.width as c_int,
                height: metadata.height as c_int,
                orientation: metadata.orientation as c_int,
                color_profile: c_string(metadata.color_profile.unwrap_or_default()),
                dpi_x: metadata.dpi_x,
                dpi_y: metadata.dpi_y,
            };
        }

        Ok(())
    }).unwrap_or(CakeImagesStatus::MetadataError)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_metadata_free(metadata: *mut CakeImagesMetadata) {
    if metadata.is_null() {
        return;
    }
    unsafe {
        free_c_string((*metadata).exif_json);
        free_c_string((*metadata).xmp_json);
        free_c_string((*metadata).iptc_json);
        free_c_string((*metadata).color_profile);
        ptr::write(metadata, std::mem::zeroed());
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_thumbnail(
    path: *const c_char,
    max_px: c_int,
    out_result: *mut CakeImagesThumbnailResult,
) -> CakeImagesStatus {
    guard_status(|| {
        if path.is_null() || out_result.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let path_str = unsafe { CStr::from_ptr(path) }.to_str()?;
        let image = Image::open(path_str).context("failed to decode for thumbnail")?;
        let thumb = image.thumbnail(max_px as u32).context("failed to generate thumbnail")?;

        let boxed = thumb.data.into_boxed_slice();
        let len = boxed.len();
        let data = Box::into_raw(boxed).cast::<u8>();

        unsafe {
            *out_result = CakeImagesThumbnailResult {
                data,
                len: len as c_ulong,
                width: thumb.width as c_int,
                height: thumb.height as c_int,
            };
        }

        Ok(())
    }).unwrap_or(CakeImagesStatus::DecodeError)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_thumbnail_free(result: *mut CakeImagesThumbnailResult) {
    if result.is_null() {
        return;
    }
    unsafe {
        let result_ref = &mut *result;
        if !result_ref.data.is_null() && result_ref.len > 0 {
            let raw_slice = ptr::slice_from_raw_parts_mut(result_ref.data, result_ref.len as usize);
            drop(Box::from_raw(raw_slice));
        }
        ptr::write(result_ref, std::mem::zeroed());
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_transform_apply(
    handle: *mut c_void,
    op: CakeImagesTransformOp,
    out_result: *mut CakeImagesTransformResult,
) -> CakeImagesStatus {
    guard_status(|| {
        if handle.is_null() || out_result.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let new_handle = with_handle(handle, |h| {
            let transformed = match op {
                CakeImagesTransformOp::Rotate90 => h.image.rotate90(),
                CakeImagesTransformOp::Rotate180 => h.image.rotate180(),
                CakeImagesTransformOp::Rotate270 => h.image.rotate270(),
                CakeImagesTransformOp::FlipHorizontal => h.image.flip_horizontal(),
                CakeImagesTransformOp::FlipVertical => h.image.flip_vertical(),
                CakeImagesTransformOp::ExifAuto => h.image.apply_exif_orientation(),
            }?;

            let new_handle = Box::into_raw(Box::new(ImageHandle {
                image: transformed,
                path: h.path.clone(),
            }));

            Ok((new_handle, true))
        }).ok_or_else(|| anyhow::anyhow!("invalid handle"))??;

        unsafe {
            *out_result = CakeImagesTransformResult {
                new_handle: new_handle.0,
                lossless: if new_handle.1 { 1 } else { 0 },
            };
        }

        Ok(())
    }).unwrap_or(CakeImagesStatus::InvalidArgument)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_images_color_profile_apply(
    handle: *mut c_void,
    profile_path: *const c_char,
    intent: c_int,
) -> CakeImagesStatus {
    guard_status(|| {
        if handle.is_null() || profile_path.is_null() {
            return Err(anyhow::anyhow!("null argument"));
        }

        let profile_path_str = unsafe { CStr::from_ptr(profile_path) }.to_str()?;
        let render_intent = match intent {
            0 => Intent::Perceptual,
            1 => Intent::RelativeColorimetric,
            2 => Intent::Saturation,
            3 => Intent::AbsoluteColorimetric,
            _ => return Err(anyhow::anyhow!("invalid intent")),
        };

        with_handle(handle, |h| {
            h.image.apply_color_profile(profile_path_str, render_intent)
        }).ok_or_else(|| anyhow::anyhow!("invalid handle"))??;

        Ok(())
    }).unwrap_or(CakeImagesStatus::ColorManagementError)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    fn create_test_image() -> NamedTempFile {
        let mut file = NamedTempFile::new().unwrap();
        let img = image::RgbImage::new(100, 100);
        img.save(&mut file, ImageFormat::Png).unwrap();
        file.flush().unwrap();
        file
    }

    #[test]
    fn decode_and_render() {
        let file = create_test_image();
        let path = CString::new(file.path().to_str().unwrap()).unwrap();

        let mut info = std::mem::zeroed::<CakeImagesInfo>();
        let handle = cake_images_decode(path.as_ptr(), &mut info);
        assert!(!handle.is_null());
        assert_eq!(info.width, 100);
        assert_eq!(info.height, 100);

        let mut result = std::mem::zeroed::<CakeImagesRenderResult>();
        let status = cake_images_render(handle, CakeImagesViewport {
            x: 0.0, y: 0.0, scale: 1.0,
            target_width: 100, target_height: 100,
        }, &mut result);
        assert_eq!(status, CakeImagesStatus::Ok);
        assert!(!result.cairo_surface.is_null());

        cake_images_free(handle);
    }

    #[test]
    fn thumbnail_generation() {
        let file = create_test_image();
        let path = CString::new(file.path().to_str().unwrap()).unwrap();

        let mut result = std::mem::zeroed::<CakeImagesThumbnailResult>();
        let status = cake_images_thumbnail(path.as_ptr(), 64, &mut result);
        assert_eq!(status, CakeImagesStatus::Ok);
        assert!(!result.data.is_null());
        assert!(result.width <= 64 && result.height <= 64);

        cake_images_thumbnail_free(&mut result);
    }
}