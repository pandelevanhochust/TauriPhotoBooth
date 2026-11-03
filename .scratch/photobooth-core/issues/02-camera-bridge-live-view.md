# 02: Camera Bridge Sidecar & Real-Time Live View Stream

**What to build:** A live camera feed rendered on the kiosk screen with horizontal mirror mode. The system spawns an independent .NET console worker (`HihiCamBridge`) as a sidecar process and communicates with it using Stdio JSON-RPC without opening network ports. When running without physical cameras connected, the system automatically uses a synthetic Mock Camera generating real-time animated frames with live timestamp overlays; when a physical USB webcam is attached, it streams DirectShow frames at 30 FPS with sub-50ms latency.

**Blocked by:** 01 (Minimal Kiosk Shell & Mock Standby Screen)

**Status:** ready-for-agent

- [ ] Tauri Core automatically launches `HihiCamBridge` on startup and monitors its process lifecycle via standard I/O streams.
- [ ] Stdio JSON-RPC protocol successfully exchanges `initialize`, `start_preview`, and `stop_preview` commands.
- [ ] Synthetic Mock Camera generates and streams animated 30 FPS frames with dynamic timestamps when no hardware camera is present.
- [ ] DirectShow camera provider successfully enumerates USB webcams and streams frames when attached.
- [ ] The React frontend renders the live stream smoothly with an optional horizontal mirror (flip) toggle.
