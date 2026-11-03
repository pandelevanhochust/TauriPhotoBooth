# 03: Posing Countdown & Automated 4-Shot Capture Loop

**What to build:** An automated 4-shot photo capture experience. When the user initiates a capture session, a 10-second countdown clock appears on screen with accelerating audio `beep.mp3` sound effects. During this 10-second posing period, the general session inactivity timeout is explicitly suspended. At the zero mark, the system triggers the camera shutter, captures a high-resolution still image, validates its readability, saves it to disk (`raw_01.jpg`), and repeats the cycle until 4 photos are captured. The UI then displays a 4-photo thumbnail review grid.

**Blocked by:** 02 (Camera Bridge Sidecar & Real-Time Live View Stream)

**Status:** ready-for-agent

- [ ] A 10-second visual countdown timer renders over the live view with audible beep prompts accelerating in the last 3 seconds.
- [ ] The 10-second countdown period does not trigger session abandonment timeouts.
- [ ] The camera captures 4 distinct full-resolution still photos sequentially into the session directory (`raw_01.jpg` through `raw_04.jpg`).
- [ ] Each captured file is validated for JPEG header integrity before proceeding to the next shot.
- [ ] Upon completing 4 shots, the UI transitions to a review screen displaying all 4 photo thumbnails.
