# 07: Thermal Print Spooler & Atomic Paper Counter in Rust

**What to build:** An automated printing pipeline connecting to dye-sublimation photo printers (DNP DS-RX1 / DS620 / Citizen CY-02) via Windows Spooler and PowerShell (`resources/scripts/print_dnp.ps1`). Tauri Core dispatches the $1200 \times 1800\text{ px}$ print sheet with calibrated zoom (0.98) and margin offsets. The system maintains an atomic, file-locked paper counter in `paper_counter.json` using Rust file locks; upon successful spooling, the paper count is decremented. When remaining paper reaches zero, the kiosk transitions into an out-of-paper lock state on the Standby screen.

**Blocked by:** 05 (Pure Rust 300 DPI Template Compositor)

**Status:** ready-for-agent

- [ ] Tauri command dispatches `resources/scripts/print_dnp.ps1` to Windows Spooler with copies, zoom, and margin parameters.
- [ ] Atomic file-locked paper counter in Rust prevents race conditions and decrements remaining sheets on each print.
- [ ] Out-of-Paper state triggers automatically when paper counter reaches 0, disabling the start button.
- [ ] Mock printer mode writes simulated print artifacts to virtual prints directory when no hardware printer is connected.
- [ ] Concurrency unit test in Rust verifies simultaneous prints decrement the counter without data corruption.
