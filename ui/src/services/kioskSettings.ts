// Cấu hình thời gian của kiosk, chỉnh trong tab "Thời gian" của AdminModal.
// Lưu bằng localStorage (WebView2 giữ lại giữa các lần chạy); host chưa có nơi lưu cấu hình.

export interface KioskSettings {
  /** Tổng thời gian cho chế độ chụp bằng nút bấm (giây). */
  remoteCaptureTimeoutSec: number;
  /** Số giây đếm ngược trước mỗi tấm ở chế độ tự động. */
  autoCountdownSec: number;
  /** Tổng số tấm chụp trong một phiên. */
  totalShots: number;
  /** Thời gian chọn bộ lọc ở bước 8 (giây). */
  filterTimeoutSec: number;
  /** Thời gian chọn ảnh ở bước 9 (giây). */
  selectTimeoutSec: number;
  /** Ghi video đầy đủ 15 khung/giây trong lúc chụp (lưu timelapse.mp4 vào thư mục gallery của phiên). */
  timelapseEnabled: boolean;
  /** Chế độ nút bấm: hiện nút "Bấm chụp" trên màn hình (ngoài nút / điều khiển từ xa). */
  screenShutterButton: boolean;
}

/** Các cài đặt dạng số (chỉnh bằng ô nhập số trong admin). */
export type NumericSettingKey = Exclude<keyof KioskSettings, 'timelapseEnabled' | 'screenShutterButton'>;

export const DEFAULT_KIOSK_SETTINGS: KioskSettings = {
  remoteCaptureTimeoutSec: 180,
  autoCountdownSec: 10,
  totalShots: 10,
  filterTimeoutSec: 60,
  selectTimeoutSec: 180,
  timelapseEnabled: true,
  screenShutterButton: true,
};

const LIMITS: Record<NumericSettingKey, [number, number]> = {
  remoteCaptureTimeoutSec: [30, 900],
  autoCountdownSec: [3, 30],
  totalShots: [1, 20],
  filterTimeoutSec: [15, 600],
  selectTimeoutSec: [30, 900],
};

const STORAGE_KEY = 'favori.kioskSettings';

function sanitize(raw: Partial<Record<keyof KioskSettings, unknown>>): KioskSettings {
  const result = { ...DEFAULT_KIOSK_SETTINGS };
  if (typeof raw.timelapseEnabled === 'boolean') result.timelapseEnabled = raw.timelapseEnabled;
  if (typeof raw.screenShutterButton === 'boolean') result.screenShutterButton = raw.screenShutterButton;
  (Object.keys(LIMITS) as NumericSettingKey[]).forEach((key) => {
    const value = Number(raw[key]);
    if (Number.isFinite(value)) {
      const [min, max] = LIMITS[key];
      result[key] = Math.min(max, Math.max(min, Math.round(value)));
    }
  });
  return result;
}

export function getKioskSettings(): KioskSettings {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) return sanitize(JSON.parse(stored));
  } catch {
    // localStorage bị chặn hoặc dữ liệu hỏng: dùng mặc định
  }
  return { ...DEFAULT_KIOSK_SETTINGS };
}

export function saveKioskSettings(partial: Partial<KioskSettings>): KioskSettings {
  const next = sanitize({ ...getKioskSettings(), ...partial });
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    // không lưu được thì giá trị chỉ có hiệu lực cho lần gọi này
  }
  return next;
}
