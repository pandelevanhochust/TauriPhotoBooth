# ADR 0006: .NET 10 WebView2 Host with React Frontend

## Context

Following pre-flight checks, the host environment has:
- .NET 10 SDK (`10.0.114`)
- Node.js `v20.16.0` & npm `10.8.1`
- Python `3.12.6`
- SQLite `3.44.3`
- However, Rust (`cargo` / `rustc`) is not installed on the system.

Installing a full Rust toolchain would introduce substantial delay and build fragility. Furthermore, in early discussions the user specifically expressed interest in .NET host capabilities if React can be used for the frontend.

## Decision

We adopt a **.NET 10 Host + WebView2 + React/TypeScript/Tailwind Frontend** architecture:
1. **Frontend:** 100% React + TypeScript + Tailwind CSS, combining UI aesthetics from both `HIHIBOOTH` and `FavoriCurrent`.
2. **Host / Backend:** C# .NET 10 desktop application embedding `Microsoft.Web.WebView2`.
3. **IPC Mechanism:** JSON messages over `window.chrome.webview.postMessage` and C# `WebMessageReceived`, providing an identical API experience to Tauri's `invoke` / `emit`.
4. **Native Capabilities:** Direct in-process access to `Magick.NET`, DirectShow, Canon EDSDK, SQLite EF Core, and Windows Spooler.

## Consequences

### Positive
- Builds and runs immediately using the pre-installed .NET 10 SDK and Node.js without requiring external compilers.
- Preserves 100% of the React frontend design flexibility and modern Kiosk UI animations.
- Native integration with `FavoriCurrent`'s `Magick.NET` and `store.db` without IPC serialization overhead.
- Satisfies all 10 tracer-bullet tickets and user stories defined in `spec.md`.

### Negative
- Application executable is a .NET assembly (`PhotoBooth.exe`) rather than a Rust PE binary.
