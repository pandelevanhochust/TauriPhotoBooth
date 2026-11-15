/**
 * Phóng to / kéo ảnh trong một ô của frame.
 * Công thức phải khớp với NativeSkiaCompositor.RenderPhotoInHole ở host để bản in giống bản xem trước.
 *
 * - scale: hệ số so với cỡ vừa phủ kín ô (1 = phủ kín, cắt giữa).
 * - x, y: độ lệch tâm ảnh so với tâm ô, tính theo tỉ lệ chiều rộng / chiều cao ô.
 */
export interface PhotoTransform {
  scale: number;
  x: number;
  y: number;
}

export const IDENTITY_TRANSFORM: PhotoTransform = { scale: 1, x: 0, y: 0 };
export const MIN_SCALE = 1;
export const MAX_SCALE = 4;

/** Kích thước ảnh đã vẽ, tính theo bội số kích thước ô (1 = bằng ô). */
export function drawnSize(photoAspect: number, holeAspect: number, scale: number) {
  const pa = photoAspect > 0 ? photoAspect : 1.5;
  const ha = holeAspect > 0 ? holeAspect : 1;
  return pa > ha
    ? { w: (pa / ha) * scale, h: scale }
    : { w: scale, h: (ha / pa) * scale };
}

/** Kẹp scale và độ lệch để ảnh luôn phủ kín ô. */
export function clampTransform(t: PhotoTransform, photoAspect: number, holeAspect: number): PhotoTransform {
  const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, Number.isFinite(t.scale) ? t.scale : 1));
  const size = drawnSize(photoAspect, holeAspect, scale);
  const maxX = Math.max(0, (size.w - 1) / 2);
  const maxY = Math.max(0, (size.h - 1) / 2);
  const clamp = (v: number, m: number) => Math.min(m, Math.max(-m, Number.isFinite(v) ? v : 0));
  return { scale, x: clamp(t.x, maxX), y: clamp(t.y, maxY) };
}

/** Style CSS (đơn vị % của ô) cho thẻ <img> nằm trong ô. */
export function transformStyle(t: PhotoTransform, photoAspect: number, holeAspect: number) {
  const size = drawnSize(photoAspect, holeAspect, t.scale);
  return {
    width: `${size.w * 100}%`,
    height: `${size.h * 100}%`,
    left: `${(0.5 + t.x - size.w / 2) * 100}%`,
    top: `${(0.5 + t.y - size.h / 2) * 100}%`,
  };
}
