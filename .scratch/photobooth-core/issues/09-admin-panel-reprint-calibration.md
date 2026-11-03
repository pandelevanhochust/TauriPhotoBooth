# 09: Admin Panel, Technician Reprint & Calibration

**What to build:** A password-protected administrative back-office for field operators. Tapping 5 times within 2 seconds on the hidden top-left corner of the Standby screen prompts for a technician PIN. Within the Admin screen, technicians can reload paper (updating paper counter), calibrate printer test prints, check camera drivers, and view recent sessions to trigger an immediate "In lại (Reprint)" without making customers re-shoot.

**Blocked by:** 08 (SQLite store.db & QR Thank You Digital Delivery)

**Status:** done

- [x] Hidden gesture (5 rapid taps on top-left corner) triggers the Admin PIN input modal.
- [x] PIN verification keypad supports quick touch input with lockout/error feedback.
- [x] Technicians can trigger test-print and verify printer readiness.
- [x] Technicians can reset or increment the paper counter (e.g. reload 350-sheet roll).
- [x] Admin panel lists recent sessions with a "Reprint" button that directly re-spools the print job.

**Implementation Summary:**
- Hidden 5-tap trigger implemented in `StandbyScreen.tsx`.
- Touch keypad PIN pad modal with 4-digit masking dots implemented in `AdminModal.tsx`.
- Multi-tab management interface for Paper Counter, Hardware Test, and Session Reprinting.
