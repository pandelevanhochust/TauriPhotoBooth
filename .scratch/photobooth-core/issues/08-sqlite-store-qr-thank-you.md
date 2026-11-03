# 08: SQLite store.db & QR Thank You Digital Delivery

**What to build:** Transaction persistence, session manifest logging, and customer digital photo delivery. Once printing is spooled, the session data is committed to a local SQLite database (`store.db`) matching the `FavoriCurrent` schema (`Transactions` and `Fileitems` tables). The UI transitions to an animated Thank-You screen displaying a dynamic QR code for smartphone downloading of digital photos (and video recap). A 15-second countdown timer runs; once expired, memory is released and the kiosk returns to the Standby screen.

**Blocked by:** 07 (Thermal Print Spooler & Paper Counter Management)

**Status:** done

- [x] Successful sessions insert records into SQLite `store.db` under `Transactions` (amount, payment method, copies, timestamp) and `Fileitems` (photo and video file paths, file type, file size).
- [x] Metadata and timing is recorded in session directory and database.
- [x] The Thank-You screen displays a prominent QR code linking to digital media.
- [x] A 15-second countdown auto-closes the session, purges photo bitmaps from memory, and resets the kiosk to Standby.
- [x] Database queries allow inspecting recent sessions and transaction history.

**Implementation Summary:**
- `DatabaseService.cs` in `PhotoBooth.Core.Data` handles WAL mode SQLite connections, table initialization, and index creation.
- `ComposingAndPrintScreen.tsx` displays QR download card and visual 15-second countdown timer.
- Verified in unit test `DatabaseService_RecordsTransactions_And_FileItems` passing with 100% success.
