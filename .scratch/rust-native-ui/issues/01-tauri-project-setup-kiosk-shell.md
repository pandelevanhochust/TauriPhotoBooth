# 01: Tauri v2 Project Setup & Borderless Kiosk Shell

**What to build:** An unattended Windows Kiosk desktop application scaffolded with Tauri v2 (Rust Core). The app launches into a borderless, exclusive topmost fullscreen presentation covering all Windows taskbars. System disruption keys (`Alt+Tab`, `Alt+F4`, `WinKey`) are intercepted using a native Win32 low-level keyboard hook. In debug mode, pressing `Esc` cleanly toggles between fullscreen and a standard window with DevTools enabled. The existing React 19 + TypeScript frontend is connected to the Tauri build and renders the animated touch-first Standby screen.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `src-tauri` is configured with Tauri v2 (`Cargo.toml`, `tauri.conf.json`) and compiles cleanly with `cargo build`.
- [ ] Application window launches in fullscreen borderless mode with `always_on_top` enabled and taskbar hidden.
- [ ] Win32 low-level keyboard hook suppresses `Alt+Tab`, `Alt+F4`, and `WinKey` during kiosk runtime.
- [ ] Pressing `Esc` in debug builds toggles fullscreen mode without exiting the process.
- [ ] React 19 frontend compiles and loads inside the Tauri window displaying the Standby screen with attract loop.
