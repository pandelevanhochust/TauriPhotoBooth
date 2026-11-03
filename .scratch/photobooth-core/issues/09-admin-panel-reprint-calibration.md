# 09: Admin Panel, Technician Reprint & Calibration

**What to build:** A password-protected administrative back-office for field operators. Tapping 5 times within 3 seconds on the hidden top-left corner of the Standby screen prompts for a technician PIN. The PIN is verified against a PBKDF2 hash (600,000 iterations + Salt) with a 60-second lockout after 5 consecutive failures. Within the Admin screen, technicians can reload paper (updating paper counter), calibrate printer zoom and offsets, select camera drivers, and view recent failed print sessions to trigger an immediate "In lại (Reprint)" without making customers re-shoot.

**Blocked by:** 08 (SQLite store.db & QR Thank You Digital Delivery)

**Status:** ready-for-agent

- [ ] Hidden gesture (5 rapid taps on top-left corner) triggers the Admin PIN input modal.
- [ ] PIN verification uses secure PBKDF2 hashing with lockout protection on 5 failed attempts.
- [ ] Technicians can adjust and test-print calibration zoom and X/Y offset values.
- [ ] Technicians can reset or increment the paper counter.
- [ ] Admin panel lists recent failed sessions with a "Reprint" button that directly spools the preserved `final_print_sheet.jpg`.
