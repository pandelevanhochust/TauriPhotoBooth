# 07: Thermal Print Spooler & Paper Counter Management

**What to build:** An automated printing integration connecting to physical photo printers via Windows Spooler and PowerShell (`resources/scripts/print_dnp.ps1`). When the customer confirms printing on the preview screen, the system dispatches the $1200 \times 1800\text{ px}$ image with calibrated zoom, offset X/Y, and quantity parameters. The system maintains an atomic paper counter file (`paper_counter.json`); upon successful spooling, the paper count is decremented. If paper reaches 0, the kiosk transitions into an out-of-paper lock state on the Standby screen.

**Blocked by:** 05 (Native Rust 300 DPI Template Compositor)

**Status:** done

- [x] Script `resources/scripts/print_dnp.ps1` successfully invokes `.NET System.Drawing.Printing` to send the print sheet to the configured Windows printer.
- [x] Printer zoom calibration (`zoom = 0.98`) and margin offsets (`offsetX`, `offsetY`) are applied accurately.
- [x] The paper counter (`paper_counter.json`) is decremented using atomic write and disk flush.
- [x] If remaining paper hits zero, the standby screen displays an Out-of-Paper alert and prevents new sessions.
- [x] In development without a physical printer, the mock printer mode writes a simulated output file to virtual prints directory.

**Implementation Summary:**
- `resources/scripts/print_dnp.ps1` implements `System.Drawing.Printing.PrintDocument` with scaling and offset rectangles.
- `AtomicPaperCounter.cs` in `PhotoBooth.Core.Storage` handles mutex concurrency and atomic writes.
- `ThermalPrinterService.cs` links PowerShell execution, paper counter decrementing, and error recovery.
- Tested and verified in `PaperCounter_Decrements_And_Persists_Correctly`.
