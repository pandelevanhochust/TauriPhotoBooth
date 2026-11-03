# 04: Retake Dialog & AI Beautify Filter Pipeline

**What to build:** Post-capture photo review, reshoot options, and optional AI facial enhancement. On the review screen, the user can click "Chụp lại (Retake)" to re-enter the capture loop for a fresh 4-shot batch (permitted at most once per session). The user can also adjust skin smoothing and skin whitening sliders (0-100%). When beautification is applied, Tauri Core invokes the Python sidecar (`hihicam-skin-bridge` running MediaPipe Face Landmarker with 478 points) to apply the beautification filters in batch and update the review thumbnails.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** ready-for-agent

- [ ] The review screen provides a "Chụp lại (Retake)" button that resets the 4 shots and restarts the capture loop.
- [ ] Retake is strictly limited to 1 time per session; subsequent visits disable or hide the retake button.
- [ ] The review UI provides interactive sliders for skin smoothing and whitening.
- [ ] When beautification is confirmed, the Python sidecar is invoked to detect facial landmarks and apply smoothing and skin tone whitening.
- [ ] Beautified images are saved alongside raw files (`beautified_01.jpg`..`04.jpg`) and refreshed on the UI.
