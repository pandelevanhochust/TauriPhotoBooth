# 04: 1-Shot Retake & Python AI Beautification Bridge

**What to build:** Post-capture photo inspection, single-shot retake, and AI skin smoothing. Customers can touch any single photo from the 4-shot review grid to retake it (strictly enforced maximum 1 retake per session). Customers can also toggle AI skin beautification. When enabled, Tauri Core invokes the Python sidecar (`resources/scripts/skin_beautify.py`) using OpenCV/MediaPipe bilateral smoothing to apply radiant facial skin smoothing without distorting eye or hair contours.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] Review screen displays all 4 captured photos in an aesthetic touch grid.
- [ ] Customers can select 1 photo to retake; re-enters capture loop for exactly that shot.
- [ ] Retake quota is strictly enforced (maximum 1 retake per session; counter disables retake button after use).
- [ ] AI beautification toggle in React UI calls Rust command to spawn Python beautification worker.
- [ ] Beautified images are written with high JPEG quality (95%) and thumbnails updated immediately on the review screen.
