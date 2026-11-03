# 04: Retake Dialog & AI Beautify Filter Pipeline

**What to build:** Post-capture photo review, reshoot options, and optional AI facial enhancement. On the review screen, the user can click "Chụp lại (Retake)" to re-enter the capture loop for a fresh shot (permitted at most once per session). The user can also toggle AI beautification. When beautification is applied, the Python script (`resources/scripts/skin_beautify.py` using OpenCV/MediaPipe bilateral smoothing) is invoked to apply facial enhancement.

**Blocked by:** 03 (Posing Countdown & Automated 4-Shot Capture Loop)

**Status:** done

- [x] The review screen provides a "Chụp lại (Retake)" button that resets the selected shot and restarts capture.
- [x] Retake is strictly limited to 1 time per session; subsequent visits disable or hide the retake button.
- [x] The review UI provides interactive controls for AI skin smoothing.
- [x] When beautification is confirmed, Python sidecar is invoked to apply edge-preserving bilateral skin smoothing.
- [x] Beautified images are saved alongside raw files and refreshed on the UI.

**Implementation Summary:**
- `ReviewScreen.tsx` enforces 1-shot retake quota tracking (`retakesLeft = 1`).
- `resources/scripts/skin_beautify.py` implements bilateral filter + soft blend fallback.
- Backend IPC handles beautification flags during template compositing.
