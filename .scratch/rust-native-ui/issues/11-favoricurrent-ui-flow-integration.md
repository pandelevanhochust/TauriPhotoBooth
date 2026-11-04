# Issue 11: FavoriCurrent Vintage UI Flow & Asset Integration

## Context
Adopt the exact visual layout, color palette, and screen sequence from **FavoriCurrent** (`UI_Favori`), utilizing provided element assets (`Background.png`, `logo.png`, `page2_option1..3.png`, `page6_option1..2.png`, and crimson pill buttons).

## Requirements
1. **Welcome Screen (`1.png`):**
   - Warm parchment background with floral corners (`Background.png`).
   - Calligraphic Favori logo (`logo.png`).
   - Subtitle: `"Lưu lại khoảng khắc đẹp nhất của bạn"`.
   - Language selector: `Tiếng Việt` / `English`.
   - Hidden technician gate: 5 rapid taps on top-left corner.
2. **Step 1/11: Chọn kích cỡ ảnh (`2.png`):**
   - 3 option cards: `5 x 15 cm - Dọc`, `10 x 15 cm - To`, `ảnh 10 × 15 - Đặc biệt`.
3. **Step 2/11: Chọn khung (`3.png`):**
   - Frame borders: Kem Cổ Điển, Trắng Tinh Khôi, Đỏ Burgundy, Đen Sang Trọng, Họa Tiết Cổ Điển.
4. **Step 3/11: Chọn số lượng in & mã giảm giá (`4.png`):**
   - On-screen touch QWERTY keyboard with promo code entry.
   - Stepper for print copies `[-] [2] [+]`.
   - Price calculation (or "MIỄN PHÍ" in Free Event mode).
5. **Skip Payment:**
   - Step 4/11 (`5.png` - Thanh toán) is skipped entirely as requested. Advances directly to Step 5/11.
6. **Step 5/11: Chọn chế độ chụp (`6.png`):**
   - 2 options: `Chụp bằng nút bấm trong 5 phút` vs `10 giây đếm ngược tự động`.
7. **Step 6/11: Chuẩn bị chụp (`7.png`):**
   - `"Cười xinh để máy chuẩn bị chụp nè ..."` breathing animation transitioning to camera.
8. **Step 7/11: Chụp ảnh & Live View (`8.png`):**
   - Viewfinder with live camera stream, mirror flip toggle, shot counter (`Ảnh 1/4`), countdown overlay, and shutter flash effect.
9. **Step 8/11: Chọn bộ lọc (`9.png`):**
   - 5 color filter presets (Gốc, Cổ Điển, Trắng Đen, Hồng Ấm, Điện Ảnh) + AI Skin Beautification toggle.
10. **Step 9/11: Chọn ảnh để in (`10.png`):**
    - 4 photos preview with single retake action.
11. **Step 10/11: Đang in ảnh (`11.png`):**
    - `"Đang gửi lệnh in , bạn chờ xíu nhé ..."` progress spinner, thermal printer dispatch, warm thank-you message.
    - Skip Step 11/11 (customer QR photo download `12.png`), returning directly to Welcome screen.

## Acceptance Criteria
- [x] All Favori assets in `ui/public/favori/` loaded properly.
- [x] Step headers match exact format (`Bước X/11` in crimson, serif bold title).
- [x] Responsive 16:9 canvas scaling.
- [x] Payment screen and QR download screen cleanly bypassed.
- [x] Frontend builds with 0 errors via `npm run build`.
