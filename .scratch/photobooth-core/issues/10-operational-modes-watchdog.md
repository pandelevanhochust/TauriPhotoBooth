# 10: Operational Modes (Free/VietQR) & Watchdog Auto-Recovery

**What to build:** Commercial deployment hardening and failure recovery. The system supports two operational modes selectable in Admin: Free Event mode (skips payment directly into posing guide) and VietQR mode. In addition, an independent Watchdog process monitors the kiosk process via periodic heartbeats (every 2 seconds); if the main UI hangs for more than 15 seconds or exits abnormally, the Watchdog terminates the stuck process and relaunches the kiosk within 3 seconds.

**Blocked by:** 09 (Admin Panel, Technician Reprint & Calibration)

**Status:** done

- [x] In `FreeEvent` mode, selecting a layout skips payment and transitions straight to the Live View pose guide.
- [x] In `VietQR` mode, selecting a layout configures transaction amount and prepares bank payment flow.
- [x] Heartbeat signal is written from the Host process to `heartbeat.txt` every 2 seconds.
- [x] If heartbeat ceases for 15 seconds (UI freeze), Watchdog terminates and restarts the kiosk process.
- [x] If the kiosk process crashes, Watchdog relaunches it within 3 seconds with incident logging.

**Implementation Summary:**
- `PhotoBooth.Watchdog\Program.cs` monitors `PhotoBooth.Host` process and `heartbeat.txt` freshness.
- `MainWindow.xaml.cs` runs a 2-second heartbeat timer writing to `%ProgramData%\HihiBooth\Data\heartbeat.txt`.
- Admin Modal provides one-touch toggle between `FreeEvent` and `PaidVietQR` modes.
