/**
 * Cỡ chữ và khoảng cách của toàn app co theo bề rộng màn hình.
 * Tailwind tính chữ, padding, khoảng cách theo rem, nên chỉ cần đổi cỡ chữ gốc của trang:
 * màn 1536 px giữ cỡ cũ (16 px), màn 1920 px to hơn 25%, tối đa 1.75 lần cho màn rất lớn.
 */
const BASE_WIDTH = 1536;
const BASE_FONT_PX = 16;
const MIN_SCALE = 1;
const MAX_SCALE = 1.75;

let currentScale = 1;

export function getUiScale(): number {
  return currentScale;
}

export function applyUiScale(): void {
  const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, window.innerWidth / BASE_WIDTH));
  currentScale = scale;
  const root = document.documentElement;
  root.style.fontSize = `${BASE_FONT_PX * scale}px`;
  root.style.setProperty('--ui-scale', String(scale));
}

/** Gọi một lần lúc khởi động: áp tỉ lệ và cập nhật khi đổi kích thước cửa sổ. */
export function installUiScale(): void {
  applyUiScale();
  window.addEventListener('resize', applyUiScale);
}
