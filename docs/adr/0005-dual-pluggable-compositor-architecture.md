# ADR 0005: Dual Pluggable Compositor Architecture (Rust Native vs ImageMagick)

## Context

We analyzed two distinct production photobooth architectures:
1. **`HIHIBOOTH`:** Employs an embedded native matrix engine (Rust/Canvas/Skia) optimized for zero-dependency, low-latency ($< 800\text{ms}$) print sheet assembly.
2. **`FavoriCurrent`:** Employs **ImageMagick via `Magick.NET` (C#)**, which provides battle-tested professional color grading, ICC color profile embedding (matching physical thermal printer ribbons), advanced unsharp masking, and animated video recap (MP4/GIF) generation.

Rather than locking the project into only one approach prematurely, we need a comparative, pluggable architecture where both engines can be evaluated, benchmarked, and chosen via configuration.

## Decision

We introduce a pluggable abstraction for image composition:
1. **Module A (Native Rust Compositor):** Lives inside Tauri Core (`src-tauri/src/compositor/rust_native.rs`). It performs fast `Center Crop Cover`, blits cropped images into apertures, overlays the transparent frame PNG, and duplicates dual strips natively.
2. **Module B (ImageMagick Compositor - FavoriCurrent Pattern):** Lives in the C# Sidecar (`HihiCamBridge/Compositor/MagickCompositor.cs`) utilizing `Magick.NET-Q8-AnyCPU`. The Tauri Core can invoke it via Stdio JSON-RPC (`composite_print_sheet` and `generate_video_recap`).
3. **Runtime Switching:** Configured in `appsettings.json` via `"CompositorBackend": "RustNative" | "ImageMagick"`.

## Comparison Matrix

| Criteria | **Module A: Native Rust (`image`)** | **Module B: ImageMagick (`Magick.NET`)** |
| :--- | :--- | :--- |
| **Execution Context** | Tauri Core (In-process Rust) | `HihiCamBridge` Sidecar (C# .NET) |
| **Dependencies** | Zero external C/C++ dependencies | Bundles ImageMagick native binaries |
| **Latency** | **Extremely Fast** ($< 400\text{ms}$) | **Fast** (~$700 - 1200\text{ms}$) |
| **Memory Footprint** | Low (~$60\text{MB}$ peak buffer) | Medium (~$150 - 200\text{MB}$ Magick buffer) |
| **Color Profiles & ICC** | Basic sRGB transform | **Advanced** (Applies printer-specific ICC profiles) |
| **Video Recap (MP4/GIF)** | Requires external ffmpeg CLI | **Native support** (Direct multi-frame Magick export) |
| **Tooling Alignment** | `template.json` hole definitions | Compatible with `Tool_Create_Frame.exe` coordinates |

## Consequences

### Positive
- Allows direct side-by-side benchmarking on the actual kiosk hardware.
- Operator can choose: maximum speed (Rust Native) vs maximum color tuning and video recap (ImageMagick).
- Keeps the system architecture modular and clean.

### Negative
- Requires maintaining two compositor implementations and test suites.
