# Spec: Rust Native Tauri Core & FavoriCurrent UI Flow Implementation

## Problem Statement

The photobooth project requires a cohesive transformation to match the exact visual design language, screen sequence, and kiosk user experience of **FavoriCurrent** (`UI_Favori`), powered by a native Rust backend (Tauri v2) on branch `feat/rust-tauri-native`.

The user has explicitly specified:
1. Match the visual aesthetic and layout of **FavoriCurrent** (`D:\WORK\TauriPhotoBooth\UI_Favori\example` screens `1.png` to `14.png` and `element` folder).
2. Use the provided assets:
   - `Background.png`: Warm parchment/cream textured background with corner flourishes (`2000 x 1125`).
   - `logo.png`: Burgundy calligraphic "Favori" logo.
   - `example_large_button.png` / `example_small_button.png`: Deep crimson pill buttons (`#7a0000`).
   - `page2_option1.png`: "5 x 15 cm - Dọc" (Dual strip 2x6).
   - `page2_option2.png`: "10 x 15 cm - To" (Postcard 4x6).
   - `page2_option3.png`: "ảnh 10 × 15 - Đặc biệt" (Postcard + keychain frame).
   - `page6_option1.png`: Remote control icon ("Chụp bằng nút bấm trong 5 phút").
   - `page6_option2.png`: Clock icon ("10 giây đếm ngược tự động").
3. **Explicit Scope Constraints:**
   - **Skip Payment:** Do NOT implement QR transfer payment or cash vendor logic (`5.png` - Bước 4/11). Transition directly from quantity/voucher (`4.png`) to capture mode (`6.png`).
   - **Skip QR Photo Download:** Do NOT show final customer QR code download screen (`12.png` - Bước 11/11). After printing (`11.png` - Bước 10/11), show a warm completion banner and return to the Welcome screen.

---

## FavoriCurrent Screen Flow Specification (11-Step Base Flow with Free Kiosk Adaptations)

The kiosk operates in a fixed 16:9 widescreen canvas ($2000 \times 1125\text{ px}$ virtual coordinate space scaled responsively to full screen).

### Step 0: Welcome & Language Selection (`example/1.png`)
- **Visuals:** Ornate corner botanical flourishes on warm cream texture (`Background.png`).
- **Header:** Top right small serif label: `"Photobooth by Favori"`.
- **Center:** Large calligraphic Favori logo (`logo.png`), with italic subtitle: `"Lưu lại khoảng khắc đẹp nhất của bạn"`.
- **Interactions:**
  - Language selection pills: `[ Tiếng Việt ]` (selected default) and `[ English ]`.
  - Primary Action: Pill button `[ Tiếp tục ]` (`#7a0000` crimson pill with white text).
  - Secret Operator Gate: 5 rapid taps on top-left corner within 2 seconds launches `AdminModal`.

### Step 1: Chọn kích cỡ ảnh (`example/2.png` - "Bước 1/11")
- **Header:** Top-left Favori logo, top-right `"Photobooth by Favori"`.
- **Step Header:** Red crimson label `"Bước 1/11"`, title `"Chọn kích cỡ ảnh"` in elegant bold serif/cursive.
- **Option Grid (3 Cards):**
  1. **Dual Strip:** `page2_option1.png` | Title: `5 x 15 cm - Dọc` | Subtitle: `In theo cặp chẵn: 2, 4, 6...` (4 shots vertical).
  2. **Postcard:** `page2_option2.png` | Title: `10 x 15 cm - To` | Subtitle: `In số lượng tùy ý, ít nhất từ 2 ảnh` (4 shots 2x2 grid).
  3. **Special Postcard + Keychain:** `page2_option3.png` | Title: `ảnh 10 × 15 - Đặc biệt` | Subtitle: `1 ảnh 10x15 kèm khung và móc khoá tự chọn`.
- **Navigation:** `[ Quay về ]` (returns to Welcome) and `[ Tiếp tục ]` (proceeds to Step 2).

### Step 2: Chọn khung ảnh (`example/3.png` - "Bước 2/11")
- **Step Header:** `"Bước 2/11"`, title `"Chọn khung"`.
- **Card Selection:** Visual thumbnails of available frame borders (Classic Cream, Elegant White, Midnight Black, Vintage Floral, Minimalist Bordeaux).
- **Navigation:** `[ Quay về ]` (back to Step 1) and `[ Tiếp tục ]` (proceeds to Step 3).

### Step 3: Chọn số lượng in và nhập mã giảm giá (`example/4.png` - "Bước 3/11")
- **Step Header:** `"Bước 3/11"`, title `"Chọn số lượng in và nhập mã giảm giá"`.
- **Two-Column Layout:**
  - **Left Column:** `"Nhập mã giảm giá :"` with input box + `[ Áp dụng ]` button, followed by an on-screen touch QWERTY keyboard (`1..0`, `Q..P`, `A..L`, `Z..M`, `SPACE`, `APPLY`).
  - **Right Column:** Elegant bordered card containing:
    - `"Chọn số lượng bản in :"` with interactive stepper `[ - ] [ 2 ] [ + ]` (steps in increments of 2 for strip layouts).
    - `"Tổng tiền thanh toán :"` displaying calculated cost or `"MIỄN PHÍ"` in Free Event mode.
    - `[ Tiếp tục ]` button (advances directly to Step 5).
    - `[ Quay về ]` button (returns to Step 2).
- **Payment Bypass:** As instructed, payment screen `5.png` is skipped entirely. Clicking `Tiếp tục` jumps straight to Step 5!

### Step 5: Chọn chế độ chụp (`example/6.png` - "Bước 5/11")
- **Step Header:** `"Bước 5/11"`, title `"Chọn chế độ chụp"`.
- **Two Option Cards:**
  1. **Remote Trigger:** `page6_option1.png` | Title: `"Chụp bằng nút bấm trong 5 phút"`.
  2. **Auto Countdown:** `page6_option2.png` | Title: `"10 giây đếm ngược tự động"`.
- **Navigation:** `[ Quay về ]` (returns to Step 3), selecting an option directly advances to Step 6.

### Step 6: Chuẩn bị chụp (`example/7.png` - "Bước 6/11")
- **Step Header:** `"Bước 6/11"`, title `"Chuẩn bị chụp"`.
- **Center Callout:** `"Cười xinh để máy chuẩn bị chụp nè ..."` in warm flowing serif typography.
- **Behavior:** 3-second animated breathing pause that auto-transitions to Live View camera feed.

### Step 7: Chụp ảnh & Live View (`example/8.png` - "Bước 7/11")
- **Header:** Top-left Favori logo with `"BƯỚC 7/11"` badge below it. Top-center subtitle: `"Nhìn vào khung hình nhé"`. Top-right `"Photobooth by Favori"`.
- **Center Viewfinder:**
  - Low-latency live video stream from camera bridge.
  - Mirrored camera toggle button (`Lật gương`).
  - Shot counter badge: `"Ảnh 1/4"`, `"Ảnh 2/4"`, etc.
  - Large animated countdown overlay (`3... 2... 1...`).
  - Shutter flash effect: 150ms screen-wide soft white glow upon capture with shutter click audio.
- **Bottom Callout:** `"Vui lòng đứng trong khung hình nhé"`.
- **Flow:** Sequentially captures 4 shots, auto-advancing to Step 8.

### Step 8: Chọn bộ lọc màu (`example/9.png` - "Bước 8/11")
- **Step Header:** `"Bước 8/11"`, title `"Chọn bộ lọc"`.
- **Filter Selector:** Real-time preview thumbnails applying color grades:
  1. `Gốc` (Natural unaltered colors)
  2. `Vintage` (Warm nostalgic tone)
  3. `Black & White` (Classic monochrome contrast)
  4. `Pastel Rose` (Soft gentle warmth)
  5. `Cinematic Cool` (Deep moody modern tone)
  6. `Mịn da AI` (Toggle bilateral beauty smoothing sidecar)
- **Navigation:** `[ Tiếp tục ]` (proceeds to Step 9).

### Step 9: Chọn ảnh để in (`example/10.png` - "Bước 9/11")
- **Step Header:** `"Bước 9/11"`, title `"Chọn ảnh để in"`.
- **Photo Grid:** Displays all 4 captured shots in large high-definition cards.
- **Features:**
  - Slots reordering / slot assignment for template apertures.
  - Single-shot retake action: `[ Chụp lại ảnh này ]` (limited to 1 retake per session to prevent queue delays).
- **Navigation:** `[ Tiếp tục ]` (proceeds to Step 10).

### Step 10: In ảnh (`example/11.png` - "Bước 10/11")
- **Step Header:** `"Bước 10/11"`.
- **Center Callout:** `"Đang gửi lệnh in , bạn chờ xíu nhé ..."` with elegant animated spinner.
- **Backend Action:**
  - Rust compositor renders $1200 \times 1800\text{ px}$ 300 DPI composite sheet using Rayon and `image` crate.
  - Atomic paper counter decrements in `paper_counter.json`.
  - Spools print job via `print_dnp.ps1` to Windows Spooler.
  - Logs session to SQLite `store.db`.
- **Completion & Reset:** Displays a brief warm `"Cảm ơn bạn đã sử dụng dịch vụ!"` note and auto-resets back to Step 0 (Welcome Screen) after 5 seconds. (QR photo download screen `12.png` is bypassed per requirements).

---

## Visual Design Tokens (Favori Vintage Aesthetic)

| Token | Value | Purpose |
|---|---|---|
| Background Color | `#f5f0ea` | Warm parchment cream tone matching `Background.png` |
| Primary Accent | `#7a0000` / `#800000` | Deep burgundy/crimson for buttons, active outlines, step badges |
| Text Primary | `#2b1e16` | Deep espresso dark brown for high-contrast titles |
| Text Secondary | `#6b5b52` | Soft warm brown for subtitles and instructions |
| Card Background | `#ffffff` | Crisp white card surfaces with rounded corners (`rounded-2xl`) |
| Card Border | `rgba(180, 150, 120, 0.35)` | Subtle warm metallic gold/bronze hairline border |
| Font Families | `'Playfair Display', 'Cormorant Garamond', Georgia, serif` | Vintage European luxury editorial look |
| Script Font | `'Alex Brush', 'Dancing Script', cursive` | Delicate calligraphic accents |

---

## Backend & Integration Architecture (Tauri v2 + Rust Core)

1. **Host:** Tauri v2 borderless fullscreen kiosk window with hardware acceleration.
2. **IPC Layer:**
   - Typed commands in `src-tauri/src/commands.rs`:
     - `get_kiosk_status() -> KioskStatus`
     - `start_capture_session() -> SessionInfo`
     - `capture_shot(index: u32) -> CaptureResult`
     - `compose_strip(request: ComposeRequest) -> ComposeResponse`
     - `print_session(copies: u32) -> PrintResponse`
     - `verify_admin_pin(pin: String) -> bool`
3. **Compositor Engine (`src-tauri/src/compositor.rs`):**
   - 300 DPI $1200 \times 1800$ canvas.
   - Exact `Center Crop Cover` algorithm to preserve facial proportions.
   - Dual-strip $600 \times 1800$ auto-duplication for $2 \times 6$ layout.
4. **Hardware & Spooler:**
   - DNP printer dispatch via Windows Spooler script.
   - Atomic file locking on `paper_counter.json`.
   - SQLite `store.db` audit ledger.
