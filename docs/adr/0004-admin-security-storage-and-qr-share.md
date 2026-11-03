# ADR 0004: Admin Security, Storage Retention & Pluggable QR Delivery

## Context

A commercial photobooth deployed at both unmanned retail locations and high-tempo private events must resolve three operational challenges:
1. Field technicians need secure on-site access to calibrate printers, load paper, reprint failed tickets, and switch modes without revealing master credentials to customers.
2. Long-term kiosk operation without manual IT intervention will eventually exhaust disk space if photo sessions accumulate indefinitely.
3. Venues often have unstable or non-existent internet connections, yet customers expect digital photo retrieval via smartphone QR codes.

## Decision

1. **Admin Security:** Protect the technician screen via PBKDF2 hashing (600,000 iterations + Salt) with a 60-second lockout after 5 consecutive failures. Access is triggered via a hidden gesture (5 rapid taps at the top-left screen region).
2. **Storage Structure & Retention:**
   - Unified directory root at `%ProgramData%\HihiBooth\Data\` (`Sessions/`, `Settings/`, `Templates/`, `Logs/`).
   - Automated disk cleanup purges sessions in `Completed` or `Aborted` state older than 7 days.
   - Sessions in `Failed` state are strictly protected from automatic deletion.
3. **Pluggable QR Delivery (`StorageUploaderInterface`):**
   - Offline / Local: Generates a QR code pointing to a local web endpoint or mock URL without halting the session.
   - Online / Cloud: Uploads the composite image and video recap to S3/R2 object storage asynchronously and displays the public CDN link QR code.

## Consequences

### Positive
- Robust offline operation: the kiosk functions completely without internet access.
- Safe disk management prevents out-of-disk crashes during multi-day events.
- Auditability: technician PIN access and manual mode changes are tracked in the log.

### Negative
- Cloud photo access requires configuring S3/R2 credentials in `appsettings.json`.
