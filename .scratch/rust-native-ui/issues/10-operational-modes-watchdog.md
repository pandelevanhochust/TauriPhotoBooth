# 10: Operational Modes & Standalone Watchdog Daemon

**What to build:** Commercial venue flexibility and unattended crash auto-recovery. The system supports two operational modes selectable in Admin: Free Event mode (skips payment directly into posing guide) and Paid VietQR mode (generates dynamic QR payment with timeout and polling listener). Additionally, an independent Watchdog process continuously monitors Tauri Core heartbeats; if the UI hangs for > 15 seconds or crashes unexpectedly, the Watchdog terminates the stuck process and relaunches the kiosk within 3 seconds.

**Blocked by:** 09 (Admin Panel, Technician Reprint & Printer Calibration)

**Status:** ready-for-agent

- [ ] In `FreeEvent` mode, selecting a layout proceeds immediately to camera live view.
- [ ] In `PaidVietQR` mode, system presents a dynamic banking QR code with transaction amount and status polling.
- [ ] Tauri Core writes a fresh timestamp to `%ProgramData%\HihiBooth\Data\heartbeat.txt` every 2 seconds.
- [ ] Independent Watchdog supervisor detects unresponsive process (> 15s heartbeat timeout) and relaunches kiosk.
- [ ] Watchdog implements startup grace period (25s) to prevent false-positive terminations during cold boot.
