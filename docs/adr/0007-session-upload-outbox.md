# ADR 0007: Session upload outbox and offline QR share links

Status: Accepted (2026-11-15). Supersedes the QR delivery and retention parts of ADR 0004 (items 2 and 3).

## Context

After each session the customer gets a printed photo with a QR code that opens a download page for the collage, the individual shots and the session video. The Favori backend (`vietqr-kiosk`, see `INTEGRATION.md`) already implements the API: `init` → upload one file per request → `complete`, a public page at `{shareBaseUrl}/{shareToken}`, and deletion 24 hours after capture.

Constraints:

- The QR must be printed immediately, even when the venue has no internet.
- Uploads must survive network loss, app restarts (Watchdog) and power loss, and must never stall live view or the next customer's capture.
- Files of one session must be sent one at a time (the backend recomputes status non-atomically).
- Customer media lives for 24 hours, then is deleted on the server and on the kiosk.

## Decision

1. **Kiosk-generated identifiers.** At `start_capture_session` the host generates a `sessionId` (UUID v4) and a `shareToken` (22 chars `[A-Za-z0-9]`, `RandomNumberGenerator`). The existing `SESSION_yyyyMMdd_HHmmss_xxxxxx` id stays as the local folder name only and is never put in a public link.
2. **Offline share URL.** `shareUrl = cachedShareBaseUrl + "/" + shareToken`. `shareBaseUrl` comes from `GET /api/kiosk/sessions/config`, cached in `cloud.json`; before the first successful fetch it falls back to `BaseUrl + "/s"`. The QR is rendered locally (QRCoder + Skia), never through an online QR service.
3. **QR placement.** When the frame has `qrX/qrY/qrSize` (percent, from the CMS), the QR (no caption) is stamped onto a separate print copy (`composite_print.jpg`). The same QR copy is printed, saved to the gallery and uploaded as the `collage`, so the customer downloads exactly what was printed (decided 2026-11-15). The last screen always shows the QR.
4. **Outbox with one manifest per session.** `%ProgramData%\HihiBooth\Data\Outbox\{sessionId}.json` holds sessionId, shareToken, capturedAt, the local folders, and every file with status `pending`/`uploaded`/`failed`, attempts and last error. It is written atomically (temp file, flush, rename). A separate outbox (rather than extending `session_info.json`) keeps the queue's state in one directory that is cheap to scan at startup and independent of the gallery layout.
5. **Queue logic in `PhotoBooth.Core`** (`SessionUploadQueue`, `KioskSessionsClient`, `ManifestStore`), with no WPF dependency, so it is unit-tested with a fake HTTP handler. The host only starts it, feeds it sessions and shows its status.
6. **What is sent, in order:** `collage` → every shot as `original` (long edge 2560 px, JPEG 90, EXIF orientation applied, all metadata including GPS dropped) → `thumbnail` (400 px) → `timelapse` index 0 (full session video at real speed) and index 1 (8x sped-up timelapse recorded in parallel from the same frames), both MP4 H.264 with `moov` moved before `mdat` so phones can play while downloading. The video is attached when encoding finishes, even if that is before the session is enqueued.
7. **Retry policy** follows the error table in `INTEGRATION.md`: exponential backoff with jitter from 30 s up to 10 min, retried until expiry. 401 pauses the whole queue for 10 min. 404/409 on a file re-declares the session. 400 or 409 on `init` marks the session `failed` for manual handling (the printed QR cannot change). 410 drops the session. 413/415 skip that file only.
8. **No interference with capture.** The queue pauses while the customer is on the capture step (`capture_step` IPC, or while the session video is recording). Upload bandwidth is capped (`maxUploadKBps`, default 2048), and image preparation runs at below-normal thread priority.
9. **Expiry.** At `capturedAt + retentionHours` the session is removed from the outbox and its local folders (`Sessions\SESSION_…` and the gallery copy) are deleted, uploaded or not. Only folders whose name equals the session's local id are deleted.
10. **Observability.** The Admin hardware tab shows pending sessions and files, the age of the oldest pending session, the last error and a warning once a session has waited more than 1 hour. The log repeats the warning every 15 minutes. The API key is never logged, and the `cloud_configure` IPC payload is not logged.

## Consequences

### Positive

- Customers get a working QR immediately, and photos appear as soon as the network allows.
- A crash or power loss loses at most the progress of one file. On restart, `init` reports what the server already has, and only the missing files are sent.
- The queue is testable without hardware or a server (`SessionUploadQueueTests`).

### Negative

- Local gallery copies are no longer kept indefinitely. They are deleted 24 hours after capture, matching the customer-facing promise.
- If the network is down for the whole 24 hours, the printed QR never works for that customer. The 1-hour warning exists so an operator can fix the network in time.
- Uploads go through the backend (one file per request, at most 150 MB). Direct-to-R2 presigned uploads can replace this later without changing `init`/`complete`.
- Kiosks without an API key configured do not print a QR at all, rather than printing a link that will never work.
