# 02: Stdio JSON-RPC Sidecar Supervisor & Live View Bridge

**What to build:** A hardware-isolated camera integration where Tauri Core spawns the .NET `HihiCamBridge` sidecar process and communicates with it using Stdio JSON-RPC over stdin/stdout without opening network ports. When running without physical cameras, the sidecar provides a synthetic 30 FPS mock camera stream with timestamps and alignment grids; when attached to a USB webcam, it streams DirectShow frames. Tauri streams JPEG frames over Tauri events (`camera_frame`) to the React UI, which renders a real-time viewfinder with an optional horizontal mirror (flip) toggle.

**Blocked by:** 01 (Tauri v2 Project Setup & Borderless Kiosk Shell)

**Status:** ready-for-agent

- [ ] Tauri Core automatically spawns and manages the `HihiCamBridge` sidecar process lifecycle via `tokio::process`.
- [ ] Stdio JSON-RPC protocol exchanges line-delimited JSON commands (`initialize`, `start_live_view`, `stop_live_view`).
- [ ] Synthetic Mock Camera generates and streams 30 FPS animated frames with dynamic timestamps when physical camera is absent.
- [ ] Tauri emits throttled `camera_frame` events to the webview.
- [ ] The React frontend renders the live camera feed smoothly with a touch-friendly horizontal mirror toggle button.
