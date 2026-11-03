# 02: Camera Bridge Sidecar & Real-Time Live View Stream

**What to build:** A live camera feed rendered on the kiosk screen with horizontal mirror mode. The system communicates using JSON-RPC without opening network ports. When running without physical cameras connected, the system automatically uses a synthetic Mock Camera generating real-time animated frames with live timestamp overlays; when a physical USB webcam is attached, it streams DirectShow frames at 30 FPS with sub-50ms latency.

**Blocked by:** 01 (Minimal Kiosk Shell & Mock Standby Screen)

**Status:** done

- [x] Host automatically manages camera provider lifecycle.
- [x] Zero-port IPC protocol successfully exchanges `get_kiosk_status`, `start_capture_session`, and `capture_shot` commands.
- [x] Synthetic Mock Camera generates and streams animated 30 FPS frames with dynamic timestamps when no hardware camera is present.
- [x] DirectShow camera provider successfully enumerates USB webcams and streams frames when attached.
- [x] The React frontend renders the live stream smoothly with an optional horizontal mirror (flip) toggle.

**Implementation Summary:**
- `MockCameraProvider.cs` generates high-resolution Skia frames at configured FPS with dynamic timestamps.
- Zero-port IPC dispatch in `MainWindow.xaml.cs` streams throttled Base64 JPEG frames via `camera_frame` events.
- `LiveViewScreen.tsx` provides touch mirror toggle (`scale-x-[-1]`) and responsive framing viewfinder.
