# GLOSSARY

Canonical terminology for the Photobooth system. Use these definitions consistently across code, specs, and discussions.

## Core Concepts

### Orchestrator (Core)
The central supervisor process (Tauri v2 in Rust) responsible for the Kiosk state machine, user session lifecycle, coordinating UI events, native 300 DPI composition, and managing child satellite processes.

### Sidecar (Satellite Process)
An independent, modular executable spawned and managed by the Orchestrator to isolate complex, platform-specific or language-specific tasks (e.g. camera hardware SDKs, heavy AI facial algorithms, ImageMagick processing).

### Camera Bridge (`HihiCamBridge`)
The dedicated .NET worker process managing physical imaging devices: Canon EOS DSLR (via EDSDK), Sony Alpha (via Camera Remote SDK), USB Webcams (via DirectShow / UVC), and Mock Camera. Communicates with the Orchestrator over standard I/O (Stdio JSON-RPC). Can optionally host the `ImageMagickCompositor` module.

### Mock Camera Driver
A built-in synthetic image provider inside `HihiCamBridge` generating real-time animated frames (dynamic timestamp overlay) and realistic capture stills without requiring physical camera hardware.

### AI Beautify Worker (`hihicam-skin-bridge`)
The Python worker process executing Google MediaPipe Face Mesh (478 landmarks) and OpenCV algorithms to apply real-time or post-capture facial enhancement (skin smoothing, skin whitening, blush, lipstick).

### Thermal Print Script
The PowerShell script (`print_dnp.ps1`) interfacing with the Windows Print Spooler (`System.Drawing.Printing`) to drive dye-sublimation thermal photo printers (DNP, Citizen, HiTi) with zoom calibration, physical margin offsets, and automated cut-strip execution.

### Template Hole Matrix (`holes_metadata`)
A coordinate mapping definition (`x`, `y`, `w`, `h`) defining transparent apertures on a 300 DPI decorative frame graphic into which cropped customer photographs are composite-rendered.

### Center Crop Cover
An image scaling algorithm that resizes a source photograph to completely cover a target bounding box while preserving aspect ratio, cropping excess pixels symmetrically from the center (horizontally or vertically) without distortion.

### Dual-Strip Duplication ($2 \times 6$ inch)
A composition method where a single $600 \times 1800\text{ px}$ vertical strip is duplicated side-by-side onto a standard $1200 \times 1800\text{ px}$ ($4 \times 6\text{ inch}$) sheet, designed for photo printers with an automated vertical splitter knife.

### Native Rust Compositor
The high-speed, zero-dependency compositing engine built directly into the Tauri Rust Core using the `image` crate, focusing on sub-second rendering and minimal RAM footprint.

### ImageMagick Compositor Module (`FavoriCurrent` Pattern)
The professional graphic processing engine built in C# via `Magick.NET`, supporting advanced ICC color profile transformations, sharpening filters, and animated video recap (MP4/GIF) generation.

### Admin Security Guard
The authentication barrier protecting technician operations (calibration, paper loading, reprint, operational mode toggle) using PBKDF2 password hashing (with random salt and 600,000 iterations) and automated lockout upon consecutive failed attempts.

### Data Retention Manager
An automated background maintenance routine purging `Completed` and `Aborted` sessions older than a configurable retention threshold (default: 7 days) while permanently preserving `Failed` sessions for technician dispute resolution.

### Storage Uploader Interface
A pluggable abstraction providing digital photo delivery via QR code: supporting offline/local hotspot links in local mode, and cloud object storage CDN links (e.g. S3 / Cloudflare R2) when internet connectivity is active.
