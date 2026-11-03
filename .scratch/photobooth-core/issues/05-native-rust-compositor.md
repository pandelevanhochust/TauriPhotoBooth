# 05: Native Rust 300 DPI Template Compositor (Module 3A)

**What to build:** An in-process, high-speed 300 DPI image compositing engine built in native Rust (`image` crate). The engine reads a decorative frame template definition (`template.json`) containing hole coordinates `{ index, x, y, w, h }`. It scales and crops the 4 captured photos using the `Center Crop Cover` algorithm to perfectly fit each aperture without stretching or distortion, blits them onto a $1200 \times 1800\text{ px}$ canvas, overlays the transparent `frame_overlay.png`, and saves a high-quality print sheet (`final_print_sheet.jpg`). For $2 \times 6$ inch layouts, the engine duplicates the left $600 \times 1800\text{ px}$ strip across to the right half.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] Parses `template.json` hole coordinates and verifies dimension validity ($1200 \times 1800\text{ px}$ at 300 DPI).
- [ ] Implements the `Center Crop Cover` algorithm to scale and center-crop photos to match hole aspect ratios without distortion.
- [ ] Correctly layers photos underneath transparent apertures of `frame_overlay.png` in sub-second execution time (< 400ms).
- [ ] When the layout is Dual Strip ($2 \times 6$ inch), duplicates the $600 \times 1800\text{ px}$ left strip onto the right half of the canvas.
- [ ] Generates a crisp, lightweight preview thumbnail sent back to the React UI for customer review.
