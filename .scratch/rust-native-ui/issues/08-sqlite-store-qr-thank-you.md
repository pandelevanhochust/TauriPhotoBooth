# 08: SQLite store.db & Thank-You QR Delivery

**What to build:** Transaction persistence, session manifest logging, and customer digital delivery. Once printing spools, session data is committed to local SQLite `store.db` using `rusqlite` matching the FavoriCurrent schema (`Transactions` and `Fileitems` tables). The UI displays an animated Thank-You screen with a dynamic QR code for smartphone downloading of digital photos and video recap. A 15-second countdown timer runs before automatically purging memory and resetting to the Standby screen.

**Blocked by:** 07 (Thermal Print Spooler & Atomic Paper Counter in Rust)

**Status:** ready-for-agent

- [ ] Successful sessions commit records to SQLite `store.db` under `Transactions` and `Fileitems` tables.
- [ ] Thank-You screen renders a dynamic QR code containing the digital gallery URL.
- [ ] 15-second countdown timer runs with automatic session cleanup and transition back to Standby.
- [ ] In-memory photo bitmaps and cached frame buffers are explicitly freed on session close.
- [ ] Rust background cleanup worker purges sessions older than 7 days from disk.
