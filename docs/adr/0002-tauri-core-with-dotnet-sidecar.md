# ADR 0002: Tauri Core Orchestrator with .NET Camera Sidecar

## Context

We evaluated whether to unify the backend into a monolithic C# .NET process hosting WebView2, or recreate the exact multi-process architecture of `HIHIBOOTH`:
1. **Option A:** Tauri v2 (Rust Core) + React Frontend + .NET Camera Sidecar.
2. **Option B:** C# .NET 10 Host (hosting WebView2 with React + native camera directly in C#).

## Decision

We choose **Option A: Tauri v2 (Rust Core) + React Frontend + .NET Camera Sidecar**.

## Consequences

### Positive
- Recreates the exact technical architecture, process isolation, and deployment structure of `HIHIBOOTH`.
- Extreme hardware fault tolerance: native C/C++ camera SDK crashes or USB disconnection in `HihiCamBridge` do not crash the customer-facing Kiosk UI.
- Lightweight host binary (~15MB), fast startup, low memory footprint.
- Clean separation of concerns: Rust manages the OS window, kiosk mode, and IPC; C# handles camera hardware; Web/React handles graphics and user experience.

### Negative
- Requires maintaining two backend codebases (Rust for Tauri, C# for `HihiCamBridge`).
- Requires inter-process communication serialization (Stdio JSON-RPC).
