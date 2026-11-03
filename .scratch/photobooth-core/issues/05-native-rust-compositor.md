# 05: Native 300 DPI Template Compositor (Module 3A)

**What to build:** An in-process, high-speed 300 DPI image compositing engine (Module 3A). The engine reads a decorative frame template definition containing hole coordinates `{ index, x, y, w, h, cornerRadius }`. It scales and crops the 4 captured photos using the `Center Crop Cover` algorithm to perfectly fit each aperture without stretching or distortion, blits them onto a $1200 \times 1800\text{ px}$ canvas, overlays the transparent `frame_overlay.png`, and saves a high-quality print sheet (`final_print_sheet.jpg`). For $2 \times 6$ inch layouts, the engine duplicates the left $600 \times 1800\text{ px}$ strip across to the right half.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** done

- [x] Parses template hole coordinates and verifies dimension validity ($1200 \times 1800\text{ px}$ at 300 DPI).
- [x] Implements the `Center Crop Cover` algorithm to scale and center-crop photos to match hole aspect ratios without distortion.
- [x] Correctly layers photos underneath transparent apertures of frame overlay in sub-second execution time (< 300ms).
- [x] When the layout is Dual Strip ($2 \times 6$ inch), duplicates the $600 \times 1800\text{ px}$ left strip onto the right half of the canvas.
- [x] Generates a crisp, lightweight preview thumbnail sent back to the React UI for customer review.

**Implementation Summary:**
- `NativeSkiaCompositor.cs` implemented in `PhotoBooth.Imaging\Compositor\`.
- Hardware-accelerated Skia surface with anti-aliasing, rounded corners clip path, and $1200 \times 1800$ canvas.
- Verified in unit test `SkiaCompositor_Generates_Valid_Composite_JPEG` passing in 150ms.
