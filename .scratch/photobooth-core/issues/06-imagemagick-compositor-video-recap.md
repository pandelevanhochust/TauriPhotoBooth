# 06: ImageMagick Compositor Module & Video Recap (Module 3B - FavoriCurrent Pattern)

**What to build:** An alternative, studio-grade image compositing and video recap module implemented in C# via `Magick.NET-Q8-AnyCPU` inside the `HihiCamBridge` sidecar. Tauri Core can dispatch compositing requests to this module via Stdio JSON-RPC when `"CompositorBackend": "ImageMagick"` is set in configuration. The module supports printer-specific ICC color profile conversion, subtle unsharp masking for thermal paper, coordinate parsing compatible with `Tool_Create_Frame.exe`, and generation of an animated 5-second MP4 video recap (`recap.mp4`).

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] C# `HihiCamBridge` integrates `Magick.NET-Q8-AnyCPU` and handles the `composite_with_magick` JSON-RPC method.
- [ ] Applies ICC color profile transformations (e.g. sRGB to thermal printer profile) and subtle unsharp masking for photo print clarity.
- [ ] Compiles captured photo bursts into an animated 5-second MP4 video recap (`recap.mp4`).
- [ ] Supports frame definitions formatted by `Tool_Create_Frame.exe`.
- [ ] System honors `"CompositorBackend"` configuration setting, dynamically routing compositing jobs to Rust Native or ImageMagick.
