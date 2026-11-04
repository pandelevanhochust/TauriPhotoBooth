# 05: Pure Rust 300 DPI Template Compositor (Module 3A)

**What to build:** An in-process, high-speed 300 DPI image compositing engine implemented in pure Rust (`image` and `rayon` crates). The engine reads template aperture specifications `{ shot_index, x, y, width, height, corner_radius }`, applies the `Center Crop Cover` algorithm to fit each raw photo into its target slot without aspect distortion, blits them onto a $1200 \times 1800\text{ px}$ canvas, overlays decorative PNG frames, and saves the final print sheet (`final_print_sheet.jpg`). For $2 \times 6$ inch dual-strip layouts, the left $600 \times 1800\text{ px}$ strip is cloned to the right half in sub-250ms total execution time.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] Rust module parses template aperture coordinates and verifies $1200 \times 1800\text{ px}$ at 300 DPI canvas constraints.
- [ ] Center Crop Cover algorithm scales and center-crops photos to aperture aspect ratios with Catmull-Rom filtering.
- [ ] Composites photos beneath transparent frame aperture overlays.
- [ ] For Dual Strip ($2 \times 6$ inch), duplicates the $600 \times 1800\text{ px}$ left strip onto the right half of the canvas.
- [ ] Unit tests in `cargo test --test compositor_tests` assert exact image dimensions ($1200 \times 1800$), file integrity, and sub-300ms execution.
