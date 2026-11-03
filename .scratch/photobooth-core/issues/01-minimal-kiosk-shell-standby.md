# 01: Minimal Kiosk Shell & Mock Standby Screen

**What to build:** An unattended Windows Kiosk application that launches automatically into a borderless, topmost fullscreen presentation. The user sees an inviting, animated Standby screen designed for touchscreens with clear visual branding and an attract loop. Touching anywhere on the screen transitions into a mock session flow. For developer convenience, pressing the Escape key in development builds exits the kiosk cleanly, while standard Windows disruptive keys (Alt+Tab, Alt+F4, WinKey) are intercepted.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] The desktop application launches into full-screen mode covering all system taskbars with `Topmost` set to true.
- [x] A low-level keyboard hook blocks `Alt+Tab`, `Alt+F4`, and the Windows Start key in production mode.
- [x] The frontend renders an animated Standby screen with invitation text and brand artwork.
- [x] Tapping anywhere on the Standby screen transitions smoothly into an active session view.
- [x] Pressing `Esc` in debug mode terminates the application cleanly.

**Implementation Summary:**
- Implemented in `PhotoBooth.Host` via `MainWindow.xaml` (`WindowStyle="None" WindowState="Maximized" Topmost="True"`).
- Installed low-level Win32 keyboard hook in `KeyboardHook.cs` intercepting Alt+Tab, Alt+F4, WinKey, with Esc toggling fullscreen.
- Touchscreen attract loop and 5-tap secret Admin PIN trigger implemented in React `StandbyScreen.tsx`.
