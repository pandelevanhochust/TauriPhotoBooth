# Specification: OpenPhotoBooth Core System (Tauri v2 + Sidecar Architecture)

Status: ready-for-agent

## Problem Statement

Commercial photobooth operators, event rental businesses, and kiosk owners need an autonomous, highly reliable, and visually captivating photobooth application running on Windows. Existing market solutions either suffer from catastrophic hardware driver crashes that freeze the user interface during live events, rely on dated desktop UI technologies that look unappealing to customers, or lock operators into expensive subscription models with vendor lock-in. 

Furthermore, operators require fine-grained control over the **image processing pipeline**: balancing between ultra-low-latency in-memory rendering (the lightweight `HIHIBOOTH` approach) and studio-grade graphic rendering with printer-specific ICC color profiling, sharpening filters, and animated video recap generation (the proven `FavoriCurrent` ImageMagick approach). Operators require an owned, modern solution that replicates the proven multi-process isolation of market leaders while offering a switchable, benchmarkable image processing engine.

## Solution

Build an enterprise-grade, clean-room photobooth application adopting the Multi-Process Sidecar architecture with a **Pluggable Dual-Engine Image Processor**:
1. **Core Orchestrator:** A lightweight Tauri v2 (Rust) host providing kiosk fullscreen isolation, native 300 DPI image compositing, safe atomic storage, and session lifecycle management.
2. **Frontend UI:** A responsive, touch-first React + TypeScript + Vite + Tailwind CSS interface rendering live previews, animations, countdowns, and administrative controls.
3. **Camera Bridge Sidecar:** An independent C# .NET 8/10 desktop console worker communicating with Canon EDSDK, Sony Camera Remote SDK, DirectShow Webcams, and a synthetic Mock Camera via Stdio JSON-RPC without network port conflicts.
4. **Dual Image Processing Engine (Switchable & Benchmarkable):**
   - **Engine A (Native Rust Matrix Compositor - `HIHIBOOTH` Pattern):** Zero-dependency, in-process Rust compositor executing fast Center Crop Cover and 300 DPI blitting in under 400ms.
   - **Engine B (ImageMagick Compositor - `FavoriCurrent` Pattern):** Built in C# via `Magick.NET-Q8-AnyCPU`, supporting printer-specific ICC color profile conversion, advanced unsharp masking, and animated video recap (MP4/GIF) compilation.
5. **AI Beautify Sidecar:** A Python worker executing Google MediaPipe Face Mesh (478 landmarks) and OpenCV for on-demand skin smoothing, whitening, and virtual makeup.
6. **Thermal Print Engine:** A Windows Spooler integration via PowerShell (`print_dnp.ps1`) supporting both DNP and Citizen thermal printers (`CyStat` SDK integration) with automated vertical strip slicing, zoom calibration, and margin offsets.
7. **Unified Storage & Admin:** Local database (`store.db` with Entity Framework SQLite) and session management with PBKDF2-secured admin panel, paper counter tracking, failed session reprinting, and dual operational modes (Free Event & VietQR).

## User Stories

1. As a walk-up customer, I want to see an eye-catching animated standby attract screen with an invitation to touch, so that I am drawn to use the photobooth.
2. As a customer, I want to touch anywhere on the standby screen to initiate a photo session, so that the process starts immediately without confusion.
3. As a customer, I want to select between a Dual Strip ($2 \times 6$ inch) and a Single Postcard ($4 \times 6$ inch) layout, so that I can decide how my final photo will be printed.
4. As an event guest at a wedding or private party, I want the system to operate in Free Event mode and skip payment, so that I can begin posing immediately.
5. As a retail customer at an unmanned kiosk, I want to see a dynamic VietQR code with exact payment amount and countdown timer, so that I can pay using my mobile banking app.
6. As a customer, I want the session to automatically cancel and return to standby if I walk away during layout selection for more than 45 seconds, so that the kiosk resets for the next guest.
7. As a customer, I want to see a fluid, low-latency Live View video stream of myself with horizontal mirror mode enabled, so that I can adjust my posture and hair as if looking into a mirror.
8. As a customer, I want to see a bold visual 10-second countdown accompanied by accelerating audio beeps before each shot, so that my friends and I know the exact instant the photo will be taken.
9. As a customer, I want the system to not count my 10-second posing countdown against any inactivity timeout, so that my session is never prematurely terminated while posing.
10. As a customer, I want the photobooth to automatically take 4 high-resolution photos in sequence with adequate preparation intervals between shots, so that we can capture different poses.
11. As a customer, I want to view all 4 captured photos on a review screen immediately after shooting, so that I can evaluate how our shots turned out.
12. As a customer, I want the option to trigger a Retake once per session if our photos are unsatisfactory, so that we have a second chance to get great photos without paying again.
13. As a customer, I want to optionally apply AI facial beautification filters (skin smoothing, skin whitening, lipstick, and blush), so that our photos look polished and flattering.
14. As a customer, I want to browse a catalog of decorative frame overlays and choose my favorite theme, so that my photos are customized for the occasion.
15. As a customer, I want to see an accurate, high-fidelity print preview of my final composed photo sheet with the chosen frame, so that I can verify the layout before committing to print.
16. As a customer, I want the option to adjust the print quantity before printing, so that every person in our group can receive a physical copy.
17. As a customer, I want the system to automatically print my photos on physical thermal photo paper with zero-margin full bleed, so that I receive a professional souvenir.
18. As a pair of customers choosing the $2 \times 6$ inch layout, I want the printer to automatically cut the $4 \times 6$ inch sheet into two identical $2 \times 6$ inch strips, so that my friend and I each get our own strip immediately.
19. As a customer, I want to see an animated printing progress screen with an estimated completion timer, so that I know the machine is actively producing my photos.
20. As a customer, I want to scan a digital QR code on the thank-you screen using my smartphone, so that I can download high-resolution digital copies of my photos AND an animated video recap (MP4/boomerang).
21. As a customer, I want the session to cleanly finish after 15 seconds on the thank-you screen, so that my personal photos are cleared from memory before the next customer steps up.
22. As a kiosk owner, I want the application to launch in a borderless fullscreen window with Topmost enabled and system hotkeys (Alt+Tab, Alt+F4, WinKey) locked down, so that customers cannot access the Windows desktop.
23. As a kiosk operator, I want to configure whether image composition is performed by the Native Rust Engine or the ImageMagick (`Magick.NET`) Engine in `appsettings.json`, so that I can compare rendering latency, memory overhead, and print color fidelity on my kiosk hardware.
24. As a kiosk operator, I want the ImageMagick engine to apply printer-specific ICC color profiles and subtle unsharp masking, so that the printed output matches studio photo lab quality.
25. As a frame designer, I want the system to support frame coordinate formats generated by visual tools like `Tool_Create_Frame.exe`, so that graphic artists can create new frames without writing JSON manually.
26. As a software developer, I want the application to automatically use a synthetic Mock Camera and Mock Printer when developing on a laptop, so that I can test the full end-to-end flow without physical hardware.
27. As a field technician, I want to tap five times on the hidden top-left zone of the standby screen to reveal a secret PIN prompt, so that I can access administrative controls without customer awareness.
28. As a field technician, I want the admin panel to be secured by a PBKDF2-hashed PIN with temporary lockout after 5 consecutive failures, so that unauthorized persons cannot tamper with kiosk settings.
29. As a field technician, I want to view the remaining paper count and reset or update it when reloading a new paper roll, so that the kiosk accurately tracks paper exhaustion.
30. As a kiosk operator, I want the application to automatically lock the standby screen and display an out-of-paper notice when remaining sheets reach 0, so that customers do not pay for prints that cannot be produced.
31. As a field technician, I want to calibrate printer zoom and horizontal/vertical offsets directly within the admin screen, so that the print sheet is perfectly aligned on any thermal printer model (DNP or Citizen).
32. As a field technician, I want to select the active camera driver (Canon DSLR, Sony Alpha, DirectShow Webcam, or Mock) and preview the live feed in Admin, so that I can adjust focus and zoom.
33. As a field technician, I want to view a list of recent failed print sessions and click a "Reprint" button, so that I can hand out photos to customers whose prints failed due to a temporary paper jam without forcing them to re-shoot.
34. As a kiosk operator, I want the system to record all transactions, session IDs, layouts, amounts, and uploaded files in a local SQLite database (`store.db`), so that I have a reliable audit trail of machine activity matching the `FavoriCurrent` schema.
35. As a kiosk operator, I want all session images and logs to be stored under `%ProgramData%\HihiBooth\Data\` with automated background deletion of completed sessions older than 7 days, so that the hard drive never fills up while failed sessions are preserved for inspection.
36. As an enterprise fleet manager, I want the kiosk to publish periodic status telemetry over MQTT, so that I can remotely monitor paper levels, revenue, and hardware health across all my deployed booths.

## Implementation Decisions

### 1. Architectural Modules & Division of Responsibility
- **Tauri Core (Rust Orchestrator):** Manages Kiosk lifecycle, state machine transitions, Win32 low-level keyboard hooks, native compositing (`RustNativeCompositor`), atomic file persistence, retention scheduling, and sidecar supervision.
- **Frontend Shell (React + TypeScript + Vite + Tailwind CSS):** Single Page Application rendering Kiosk views inside WebView2. Communicates with Tauri Core exclusively via structured `invoke` commands and typed events.
- **Camera Bridge & Magick Sidecar (`HihiCamBridge` - C# .NET 8/10 Console):** Satellite process managing Canon EDSDK, Sony Remote SDK, DirectShow, Mock Camera, AND hosting the **`ImageMagickCompositor`** (`Magick.NET-Q8-AnyCPU`). Interacts with Core via Stdio JSON-RPC.
- **AI Beautify Sidecar (`hihicam-skin-bridge` - Python PyInstaller):** Batch image worker bundling Google MediaPipe `face_landmarker.task` (478 points) and OpenCV for post-capture beautification.
- **Thermal Print Engine (PowerShell Spooler):** Script `resources/print_dnp.ps1` wrapping `.NET System.Drawing.Printing` and Citizen `CyStat` API to handle margins, scaling, and printer status queries.

### 2. Dual Image Processing Architecture & Comparison Design

The system implements a pluggable `IImageCompositor` abstraction allowing direct side-by-side benchmarking:

```text
               ┌────────────────────────────────────────────────────────┐
               │              Tauri Core (Rust Orchestrator)            │
               └───────────────────────────┬────────────────────────────┘
                                           │
                        Config: "CompositorBackend"
                                           │
                 ┌─────────────────────────┴─────────────────────────┐
                 │                                                   │
        [Backend == "RustNative"]                          [Backend == "ImageMagick"]
                 │                                                   │
                 ▼                                                   ▼
    ┌──────────────────────────┐                        ┌──────────────────────────┐
    │  Module 3A: Native Rust  │                        │   Module 3B: Magick.NET  │
    │   Compositor Engine      │                        │     (FavoriCurrent)      │
    │    (HihiBooth Style)     │                        │  in HihiCamBridge Sidecar│
    └────────────┬─────────────┘                        └────────────┬─────────────┘
                 │                                                   │
    • In-process `image` crate                          • Magick.NET-Q8-AnyCPU
    • Sub-second latency (<400ms)                       • ICC Color Profile Embedding
    • Ultra-low memory (<60MB)                          • Unsharp Masking / Color Balance
    • Deterministic 300 DPI                             • Animated Video Recap (MP4/GIF)
    • Output: final_print_sheet.jpg                     • Output: final_print_sheet.jpg
                                                        • Output: recap.mp4 (FileType: video)
```

#### Module 3A: Native Rust Compositor Engine (`HihiBooth` Pattern)
- Built directly into Tauri Core using the `image` and `imageproc` crates.
- Reads `template.json` defining hole apertures `{ index, x, y, w, h }`.
- Executes `Center Crop Cover`: calculates uniform scale ratio $\max(w/W_{photo}, h/H_{photo})$, scales image, and symmetrically clips excess pixels from the center.
- Blits cropped images into coordinate apertures on a $1200 \times 1800$ canvas, then overlays the transparent `frame_overlay.png`.
- For $2 \times 6$ inch layouts: duplicates the left $600 \times 1800$ strip across to the right half ($600 \le x < 1200$).
- Benchmark Target: Render time under **$400\text{ms}$**, memory peak under **$60\text{MB}$**.

#### Module 3B: ImageMagick Compositor Module (`FavoriCurrent` Pattern via `Magick.NET`)
- Built inside the C# .NET Sidecar using `Magick.NET-Q8-AnyCPU` (v14.10.1).
- Invoked via Stdio JSON-RPC:
  `{"method": "composite_with_magick", "params": {"session_id": "...", "template_path": "...", "icc_profile": "dnp_rx1.icc", "generate_video": true}}`
- Features:
  1. **Color Profile Transformation:** Reads raw sRGB camera JPEGs, converts them through the printer manufacturer's ICC color profile (e.g. `DNP_DS_RX1HS.icc` or `Citizen_CX02.icc`) for photorealistic print rendition.
  2. **Image Enhancement:** Applies subtle unsharp masking (`UnsharpMask(radius: 1.5, sigma: 1.0, amount: 0.5, threshold: 0.05)`) to counteract dye-sublimation thermal print softness.
  3. **Video Recap Generation:** Assembles captured shots and burst buffer frames into a 5-second 30 FPS MP4 video recap (`FileType: video` in SQLite `store.db`).
  4. **Tooling Compatibility:** Directly parses frame definitions generated by `Tool_Create_Frame.exe`.
- Benchmark Target: Render time under **$1.2\text{s}$**, memory peak under **$180\text{MB}$**.

#### Comparative Evaluation Matrix

| Metric / Dimension | **Module 3A: Native Rust Engine** | **Module 3B: ImageMagick Engine (`FavoriCurrent`)** |
| :--- | :--- | :--- |
| **Execution Context** | In-process (Tauri Core) | Out-of-process (HihiCamBridge Sidecar) |
| **External Dependencies** | None (pure Rust crate) | Native ImageMagick C++ binaries (`Magick.Native`) |
| **Composite Latency** | **$300 - 450\text{ ms}$ (Winner)** | $800 - 1400\text{ ms}$ |
| **Memory Footprint** | **$\approx 50 - 65\text{ MB}$ (Winner)** | $\approx 150 - 210\text{ MB}$ |
| **Color Fidelity (Print)** | Standard sRGB gamut | **Exact ICC Printer Profile Mapping (Winner)** |
| **Video Recap (MP4)** | Requires separate external ffmpeg | **Native multi-frame video export (Winner)** |
| **Frame Designer Tool** | Manual JSON / Web-based tool | Native compatibility with `Tool_Create_Frame.exe` |

### 3. Inter-Process Communication (IPC) Contracts
- **Tauri Core $\leftrightarrow$ Web Frontend:**
  - `start_session(mode: OperationMode)` $\rightarrow$ `SessionInfo`
  - `select_layout(layout: LayoutType)` $\rightarrow$ `void`
  - `start_live_view()` $\rightarrow$ Streams Base64 JPEG frames via event `camera://frame`
  - `trigger_capture(shot_index: u32)` $\rightarrow$ `CapturedShotResult`
  - `apply_beautification(params: BeautifyParams)` $\rightarrow$ `BeautifiedShotResult`
  - `render_print_sheet(template_id: String)` $\rightarrow$ Generates 300 DPI composite (via configured backend) and returns preview thumbnail
  - `submit_print_job(quantity: u32)` $\rightarrow$ `PrintJobStatus`
  - `admin_authenticate(pin: String)` $\rightarrow$ `AdminSessionToken`
- **Tauri Core $\leftrightarrow$ `HihiCamBridge` (.NET Sidecar via Stdio JSON-RPC):**
  - `initialize(driver: "Canon" | "Sony" | "DirectShow" | "Mock")`
  - `start_preview(fps: 30, resolution: "1024x768")`
  - `capture(shot_index: 1, output_path: "...")`
  - `stop_preview()`
  - `composite_with_magick(photos: string[], template: string, output_sheet: string, generate_video: bool)` $\rightarrow$ `CompositingResult`

### 4. Storage & Persistence Schema (Aligned with `FavoriCurrent` Schema)
- Base directory: `%ProgramData%\HihiBooth\Data\`
  - `Settings/appsettings.json`: Configuration with `SchemaVersion` and `"CompositorBackend"`.
  - `Settings/paper_counter.json`: Atomic file write with disk flush for remaining paper sheets.
  - `Sessions/<SessionId>/`: Contains `session_manifest.json`, `raw_01.jpg`..`04.jpg`, `final_print_sheet.jpg`, `recap.mp4`.
  - `Database/store.db`: SQLite database storing:
    - `Transactions`: `id`, `SessionId`, `TotalAmount`, `TotalImages`, `DateCreated`.
    - `Fileitems`: `id`, `FileName`, `FileType` (`image` or `video`), `DateCreated`, `SessionId`, `Layout` (`2x6`, `4x6`, `4x6_custom`), `Landscape`, `UploadStatus`.
- Background cleanup worker: Runs every 60 minutes, removing `Completed` or `Aborted` sessions older than 7 days. `Failed` sessions are exempted from deletion.

## Testing Decisions

### What Makes a Good Test
- Tests must verify observable system behavior at clean seams rather than internal implementation details.
- Tests must execute deterministically without requiring connected physical cameras or printers.
- Compositor tests must benchmark both **Native Rust** and **ImageMagick** engines against identical synthetic test inputs to verify dimensional outputs ($1200 \times 1800\text{ px}$ at 300 DPI) and compare execution times.

### Primary Testing Seams
1. **Compositing Engine Benchmark Seam:**
   - Feeds a standard 4-image set into both `RustNativeCompositor` and `ImageMagickCompositor`.
   - Asserts both produce identical $1200 \times 1800\text{ px}$ output dimensions.
   - Measures memory allocation and execution duration using `Stopwatch` / `Instant`.
2. **Session State Machine Seam:**
   - Test fixture executes complete session lifecycles (Standby $\rightarrow$ Layout $\rightarrow$ Countdown $\rightarrow$ Capture $\rightarrow$ Retake $\rightarrow$ Frame $\rightarrow$ Print) against Mock Camera and Mock Spooler.
   - Verifies timeout cancellations (45s layout timeout resets to Standby; 10s countdown suspends timeout).
3. **Sidecar Protocol Seam (Stdio JSON-RPC):**
   - Unit tests pipe JSON-RPC commands into `HihiCamBridge` over stdin and validate response formatting, error codes, base64 frame stream pacing, and `composite_with_magick` dispatch.
4. **Database Persistence Seam:**
   - Verifies that upon print completion, records are properly committed to SQLite `store.db` across both `Transactions` and `Fileitems` tables.

### Prior Art
- Prior verification test suite in `D:\WORK\PhotoBooth\tests\PhotoBooth.Tests\` (`M0VerificationTests.cs` and `M0SmokeBenchmark.cs`), confirming that mock providers and atomic disk writes run in under 800ms.

## Out of Scope
- Direct thermal printer dye-sublimation firmware flashing or USB bulk transfer reverse-engineering (printing relies on Windows Spooler and official vendor drivers).
- Online credit card POS terminal chip-and-pin integration (payment for MVP is strictly VietQR dynamic banking transfer or Free Event mode).
- Cloud administration web dashboard (telemetry is published via MQTT, but the central multi-tenant cloud dashboard is post-MVP).

## Further Notes
- Hardware target is Windows 10/11 64-bit, with recommendation for Windows 11 IoT Enterprise LTSC 2024 for long-term kiosk unattended deployments.
- Clean-room compliance: All code, UI assets, and templates will be authored from scratch without copying binary or graphic assets from the reference folders.
