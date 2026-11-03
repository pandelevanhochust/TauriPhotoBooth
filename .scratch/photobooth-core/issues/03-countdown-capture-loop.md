# 03: Posing Countdown & Automated 4-Shot Capture Loop

**What to build:** An automated 4-shot photo capture experience. When the user initiates a capture session, a countdown clock appears on screen with accelerating audio sound effects. During this posing period, the general session inactivity timeout is explicitly suspended. At the zero mark, the system triggers the camera shutter, captures a high-resolution still image, validates its readability, saves it to disk (`raw_01.jpg`), and repeats the cycle until 4 photos are captured. The UI then displays a 4-photo thumbnail review grid.

**Blocked by:** 02 (Camera Bridge Sidecar & Real-Time Live View Stream)

**Status:** done

- [x] A visual countdown timer renders over the live view with audible synthesized beep prompts accelerating near zero.
- [x] The countdown period does not trigger session abandonment timeouts.
- [x] The camera captures 4 distinct full-resolution still photos sequentially into the session directory (`raw_01.jpg` through `raw_04.jpg`).
- [x] Each captured file is validated for JPEG header integrity before proceeding to the next shot.
- [x] Upon completing 4 shots, the UI transitions to a review screen displaying all 4 photo thumbnails.

**Implementation Summary:**
- `LiveViewScreen.tsx` incorporates Web Audio oscillator synthesis for countdown and shutter beeps.
- Screen flash overlay animates synchronously with shutter fire.
- Photos are persisted sequentially in session directory and delivered as thumbnails to `ReviewScreen.tsx`.
