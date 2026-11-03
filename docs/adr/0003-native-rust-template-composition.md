# ADR 0003: Native Rust Template Composition & Dual-Strip Rendering

## Context

A photobooth session produces 4 high-resolution photos (typically 12MP to 24MP each from DSLR cameras). These photos must be scaled, center-cropped, and composed into decorative frame templates (`holes_metadata`) to produce a print-ready 300 DPI canvas ($1200 \times 1800\text{ px}$).

Performing this composition on the Web frontend (via HTML5 Canvas) risks:
1. Significant memory overhead and risk of WebView2 canvas context crashes.
2. Loss of DPI metadata and color profiling in browser canvas export.
3. Slow rendering latency blocking the UI thread.

## Decision

We perform all final image composition in the **Tauri Core (Native Rust)** using the `image` / `imageproc` crates:
1. Load high-res captured photos into memory.
2. Apply `Center Crop Cover` algorithm to fit each target aperture box `(x, y, w, h)`.
3. Blit the cropped photos onto the blank canvas at their designated hole coordinates.
4. Overlay the transparent frame PNG on top (Z-order: Background Canvas $\rightarrow$ Photos $\rightarrow$ Frame Overlay with transparent holes).
5. For $2 \times 6$ inch layouts, duplicate the left $600 \times 1800\text{ px}$ strip across to the right half to form a complete $1200 \times 1800\text{ px}$ print sheet.
6. Export as high-quality sRGB JPEG at 300 DPI (`final_print_sheet.jpg`).

## Consequences

### Positive
- Sub-second composition time (< 0.8s).
- Deterministic 300 DPI output with exact pixel coordinates.
- Web UI only receives a lightweight preview thumbnail, keeping memory usage minimal.

### Negative
- Requires template definitions (`template.json`) to be parsed and validated natively in Rust.
