# 09: Admin Panel, Technician Reprint & Printer Calibration

**What to build:** A password-protected administrative back-office for field operators. Tapping 5 times within 2 seconds on the hidden top-left corner of the Standby screen prompts for a technician PIN. The PIN is validated in Rust using PBKDF2 hashing with a lockout penalty after 5 failed attempts. Within the Admin screen, technicians can reload paper (resetting counter to 350 sheets), dispatch calibration test prints, verify camera feeds, and browse recent sessions to trigger an immediate "In lại (Reprint)" without re-shooting.

**Blocked by:** 08 (SQLite store.db & Thank-You QR Delivery)

**Status:** ready-for-agent

- [ ] Hidden 5-tap gesture on the Standby screen opens the touch PIN pad.
- [ ] PBKDF2 PIN verification in Rust with salt and 5-attempt lockout security.
- [ ] Technicians can reset paper count to 350 sheets or custom value upon loading paper roll.
- [ ] "In thử nghiệm (Test Print)" sends a diagnostic grid pattern to the thermal printer.
- [ ] Admin panel lists recent sessions from `store.db` with a one-touch "In lại (Reprint)" action.
