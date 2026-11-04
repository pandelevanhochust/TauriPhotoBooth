# 03: Posing Countdown & Automated 4-Shot Capture Loop

**What to build:** An automated 4-shot photo capture loop. Tapping the start button triggers a posing countdown with accelerating audio sound effects generated via Web Audio API. When countdown hits zero, Tauri Core invokes the sidecar's shutter capture command, saves the high-resolution still image to the session directory (`raw_01.jpg`..`raw_04.jpg`), and flashes the screen white. The process repeats automatically until 4 photos are captured, after which the UI transitions to a 4-photo review grid.

**Blocked by:** 02 (Stdio JSON-RPC Sidecar Supervisor & Live View Bridge)

**Status:** ready-for-agent

- [ ] Visual countdown ring and digits render over the live view with audible synthesized beeps accelerating in the last 3 seconds.
- [ ] During the countdown, session abandonment timeouts are suspended.
- [ ] High-resolution photos are captured sequentially into `%ProgramData%\HihiBooth\Data\Sessions\<SessionId>\raw_01.jpg` through `raw_04.jpg`.
- [ ] Screen displays a momentary white flash and shutter sound on each capture event.
- [ ] Upon completing 4 shots, UI transitions automatically to the photo review grid.
