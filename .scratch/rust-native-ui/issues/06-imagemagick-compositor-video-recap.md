# 06: ImageMagick Compositor Sidecar & Video Recap (Module 3B)

**What to build:** An alternative, studio-grade image compositing and video recap module matching the FavoriCurrent architecture. When `"CompositorBackend": "ImageMagick"` is set, Tauri Core delegates compositing to the .NET sidecar using `Magick.NET` for printer-calibrated ICC profile conversion and unsharp masking. Additionally, the module compiles captured photo bursts into an animated multi-frame video recap (`recap.gif` / `recap.mp4`) for social sharing.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] Tauri Core routes compositing commands to `HihiCamBridge` sidecar when ImageMagick backend is selected.
- [ ] ImageMagick applies ICC printer profiles and unsharp masking for thermal paper calibration.
- [ ] Photo bursts are assembled into an animated video recap (`recap.gif` / `recap.mp4`).
- [ ] System seamlessly falls back between Rust Native (Module 3A) and ImageMagick (Module 3B) based on configuration.
