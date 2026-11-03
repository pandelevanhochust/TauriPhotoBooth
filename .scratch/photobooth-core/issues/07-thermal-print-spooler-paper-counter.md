# 07: Thermal Print Spooler & Paper Counter Management

**What to build:** An automated printing integration connecting to physical photo printers via Windows Spooler and PowerShell (`resources/print_dnp.ps1`). When the customer confirms printing on the preview screen, Tauri Core dispatches the $1200 \times 1800\text{ px}$ image with calibrated zoom, offset X/Y, and quantity parameters. The system maintains an atomic paper counter file (`paper_counter.json`); upon successful spooling, the paper count is decremented. If paper reaches 0, the kiosk transitions into an out-of-paper lock state on the Standby screen.

**Blocked by:** 05 (Native Rust 300 DPI Template Compositor)

**Status:** ready-for-agent

- [ ] Script `resources/print_dnp.ps1` successfully invokes `.NET System.Drawing.Printing` to send the print sheet to the configured Windows printer.
- [ ] Printer zoom calibration (`zoom = 0.98`) and margin offsets (`offsetX`, `offsetY`) are applied accurately.
- [ ] The paper counter (`paper_counter.json`) is decremented using atomic write and disk flush.
- [ ] If remaining paper hits zero, the standby screen displays an Out-of-Paper alert and prevents new sessions.
- [ ] In development without a physical printer, the mock printer mode writes a simulated output file to `Data/VirtualPrints/`.
