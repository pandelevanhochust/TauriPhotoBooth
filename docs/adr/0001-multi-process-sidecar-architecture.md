# ADR 0001: Multi-Process Sidecar Architecture with Stdio IPC

## Context

A commercial Photobooth kiosk must simultaneously handle:
1. Fast, hardware-accelerated UI rendering (touchscreen, animations, countdowns).
2. Complex native C/C++ hardware drivers for specialized cameras (Canon EDSDK, Sony Remote SDK, Windows DirectShow).
3. Heavy facial landmark detection and computer vision filters (Google MediaPipe 478 points, OpenCV).
4. Reliable thermal photo printing (Windows Spooler, DNP/Citizen/HiTi).

Attempting to pack native camera C-bindings, Python AI packages, and Windows printing into a single monolithic binary creates significant stability, crash-cascade, and driver conflict risks.

## Decision

We adopt a **Multi-process Sidecar Pattern** matching the architecture of the reference system:
- **Core Orchestrator:** Manages Kiosk state machine, UI dispatch, and process lifecycles.
- **Camera Bridge (`HihiCamBridge`):** A dedicated .NET worker handling Canon/Sony/Webcam SDKs, communicating with Core via **Stdio JSON-RPC**.
- **AI Beautify Worker (`hihicam-skin-bridge`):** A Python/MediaPipe worker handling facial beautification on-demand.
- **Printing:** Driven via PowerShell script interfacing with Windows Spooler (`System.Drawing.Printing`).

## Consequences

### Positive
- **Fault Isolation:** If a camera driver freezes or crashes, the Core Kiosk UI does not crash and can cleanly restart the sidecar.
- **Clean Hardware Abstraction:** The Core only talks to a uniform JSON-RPC protocol (`start_preview`, `capture`, `get_devices`), regardless of whether the physical camera is Canon, Sony, or Webcam.
- **Zero Port Conflict:** Stdio IPC avoids binding to localhost TCP ports, preventing firewall blocks or antivirus interference at venues.

### Negative
- Multi-language toolchain (requires managing Rust/C#/.NET/Python build artifacts).
- Frame streaming requires Base64/binary IPC serialization over standard streams.
