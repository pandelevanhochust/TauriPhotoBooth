# 10: Operational Modes (Free/VietQR) & Watchdog Auto-Recovery

**What to build:** Commercial deployment hardening and failure recovery. The system supports two operational modes selectable in Admin: Free Event mode (skips payment directly into posing guide) and VietQR mode (generates dynamic QR payment with timeout and long-poll/webhook confirmation). In addition, an independent Watchdog process monitors the kiosk process via periodic heartbeats (every 2 seconds); if the main UI hangs for more than 15 seconds or exits abnormally, the Watchdog terminates the stuck process and relaunches the kiosk within 3 seconds.

**Blocked by:** 09 (Admin Panel, Technician Reprint & Calibration)

**Status:** ready-for-agent

- [ ] In `FreeEvent` mode, selecting a layout skips payment and transitions straight to the Live View pose guide.
- [ ] In `VietQR` mode, selecting a layout displays a dynamic VietQR code with transaction amount and polling listener for bank payment confirmation.
- [ ] Heartbeat signal is sent from the Tauri Core UI thread to the Watchdog process every 2 seconds.
- [ ] If heartbeat ceases for 15 seconds (UI freeze), Watchdog terminates and restarts the kiosk process.
- [ ] If the kiosk process crashes, Watchdog relaunches it within 3 seconds (with backoff guard against rapid consecutive crashes).
