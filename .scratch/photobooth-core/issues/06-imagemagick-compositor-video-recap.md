# 06: ImageMagick Compositor Module & Video Recap (Module 3B - FavoriCurrent Pattern)

**What to build:** An alternative, studio-grade image compositing and video recap module implemented via `Magick.NET-Q8-AnyCPU`. The module supports printer-specific ICC color profile conversion, subtle unsharp masking for thermal paper, coordinate parsing compatible with `Tool_Create_Frame.exe`, and generation of an animated video recap (`recap.gif` / `recap.mp4`).

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** done

- [x] Host integrates `Magick.NET-Q8-AnyCPU` and handles the `composite_with_magick` method.
- [x] Applies ICC color profile transformations and subtle unsharp masking for photo print clarity.
- [x] Compiles captured photo bursts into an animated video recap.
- [x] Supports frame definitions formatted by `Tool_Create_Frame.exe`.
- [x] System honors compositor selection, dynamically routing compositing jobs to Skia Native or ImageMagick.

**Implementation Summary:**
- `MagickCompositor.cs` implemented in `PhotoBooth.Imaging\Compositor\`.
- Generates 300 DPI composite JPEG with density and color profile.
- Generates animated multi-frame slideshow recap via `CreateAnimatedRecapAsync`.
- Verified in unit test `MagickCompositor_Generates_Composite_And_GifRecap` passing with 100% success.
