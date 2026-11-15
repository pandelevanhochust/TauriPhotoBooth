import { ipc } from './ipc';

export interface PrinterQueue {
  name: string;
  driver: string;
  port: string;
  isDefault: boolean;
  isVirtual: boolean;
  isPhotoPrinter: boolean;
  /** 'Ready' | 'Printing' | 'OutOfPaper' | 'PaperJammed' | 'CoverOpen' | 'Offline' | 'Unknown' */
  status: string;
  statusText: string;
  jobCount: number;
}

/**
 * Căn chỉnh bản in (lưu ở host, áp dụng cho mọi lần in). Cùng cách tính với app FavoriCurrent:
 * ảnh danh nghĩa 4x6 inch nhân tỉ lệ, đặt giữa trang rồi dịch theo offset (1/100 inch).
 */
export interface PrintCalibration {
  scalePercent: number;
  offsetX: number;
  offsetY: number;
  stripScalePercent: number;
  stripOffsetX: number;
  stripOffsetY: number;
  /** Khoảng hở thêm giữa hai dải ở đường cắt, mm. */
  stripGapMm: number;
}

/** Tình trạng máy in do host báo về (hàng đợi in của Windows). */
export interface PrinterStatus {
  ready: boolean;
  message: string;
  /** Máy in đang dùng ('' = chưa có). */
  selected: string;
  /** Máy in admin đã chọn ('' = tự chọn máy in ảnh). */
  configured: string;
  /** Máy in USB đã cắm nhưng Windows chưa có driver. */
  usbWithoutDriver: string[];
  paperCount: number;
  /** Các khổ giấy driver của máy đang dùng hỗ trợ. */
  paperSizes: string[];
  printers: PrinterQueue[];
  calibration: PrintCalibration;
}

const OFFLINE: PrinterStatus = {
  ready: false,
  message: 'Không có host (đang chạy trên trình duyệt)',
  selected: '',
  configured: '',
  usbWithoutDriver: [],
  paperCount: 0,
  paperSizes: [],
  printers: [],
  calibration: {
    scalePercent: 101,
    offsetX: -3,
    offsetY: -4,
    stripScalePercent: 101,
    stripOffsetX: 0,
    stripOffsetY: -4,
    stripGapMm: 0,
  },
};

function normalize(raw: unknown): PrinterStatus {
  if (!raw || typeof raw !== 'object' || !('printers' in raw)) return OFFLINE;
  return { ...OFFLINE, ...(raw as Partial<PrinterStatus>) };
}

export const printerApi = {
  async getStatus(): Promise<PrinterStatus> {
    try {
      return normalize(await ipc.invoke('printer_status'));
    } catch {
      return OFFLINE;
    }
  },

  /** Chọn máy in dùng cho booth; name rỗng = tự chọn máy in ảnh. */
  async select(name: string): Promise<PrinterStatus> {
    try {
      return normalize(await ipc.invoke('printer_select', { name }));
    } catch {
      return OFFLINE;
    }
  },

  /** Lưu căn chỉnh bản in. */
  async calibrate(calibration: PrintCalibration): Promise<PrinterStatus> {
    try {
      return normalize(await ipc.invoke('printer_calibrate', calibration));
    } catch {
      return OFFLINE;
    }
  },

  /** In một trang thử 4x6. Lỗi thì ném Error với thông báo của host. */
  testPrint(): Promise<{ success: boolean; message?: string }> {
    return ipc.invokeStrict('print_test_page', undefined, 60000);
  },
};
