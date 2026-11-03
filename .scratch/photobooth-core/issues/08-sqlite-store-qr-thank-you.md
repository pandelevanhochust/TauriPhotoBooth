# 08: SQLite store.db & QR Thank You Digital Delivery

**What to build:** Transaction persistence, session manifest logging, and customer digital photo delivery. Once printing is spooled, the session data is committed to a local SQLite database (`store.db`) matching the `FavoriCurrent` schema (`Transactions` and `Fileitems` tables). The UI transitions to an animated Thank-You screen displaying a dynamic QR code for smartphone downloading of digital photos (and video recap if generated). A 15-second countdown timer runs; once expired, memory is released and the kiosk returns to the Standby screen.

**Blocked by:** 07 (Thermal Print Spooler & Paper Counter Management)

**Status:** ready-for-agent

- [ ] Successful sessions insert records into SQLite `store.db` under `Transactions` (amount, images count, timestamp) and `Fileitems` (photo and video file paths, layout, upload status).
- [ ] A `session_manifest.json` is written to the session directory recording all metadata and timing.
- [ ] The Thank-You screen displays a prominent QR code linking to digital media (via local web endpoint or cloud CDN URL).
- [ ] A 15-second countdown auto-closes the session, purges photo bitmaps from memory, and resets the kiosk to Standby.
- [ ] Background cleaner routine purges `Completed` and `Aborted` session folders older than 7 days, leaving `Failed` sessions untouched.
