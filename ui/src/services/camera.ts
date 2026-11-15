import { ipc } from './ipc';

/** Trạng thái máy ảnh nối dây (Canon EDSDK / FUJIFILM X SDK) do host báo về. */
export interface CameraStatus {
  /** 'Canon' | 'Fujifilm' | '' (chưa có máy). */
  vendor: string;
  sdkAvailable: boolean;
  connected: boolean;
  liveView: boolean;
  model: string;
  port: string;
  /** 'ready' | 'liveview' | 'capturing' | 'disconnected' | 'error' */
  status: string;
  message: string;
  /** 0–100, -1 khi cắm nguồn ngoài, null khi không đọc được. */
  batteryLevel: number | null;
}

export interface ShotReady {
  shotIndex: number;
  filePath: string;
  base64: string;
}

export interface ShotFailed {
  shotIndex: number;
  message: string;
}

export interface CameraTestShot {
  filePath: string;
  resolution: string;
  fileSize: number;
  previewBase64: string;
}

const OFFLINE: CameraStatus = {
  vendor: '',
  sdkAvailable: false,
  connected: false,
  liveView: false,
  model: '',
  port: '',
  status: 'error',
  message: 'Không có host (đang chạy trên trình duyệt)',
  batteryLevel: null,
};

function normalize(raw: unknown): CameraStatus {
  if (!raw || typeof raw !== 'object' || !('connected' in raw)) return OFFLINE;
  return { ...OFFLINE, ...(raw as Partial<CameraStatus>) };
}

export const cameraApi = {
  /**
   * Chụp một tấm cho phiên. Máy ảnh chưa kết nối thì host lưu ảnh webcam trong base64.
   * Máy ảnh: host trả về ngay khi màn trập nhả (pending = true); ảnh tới sau qua onShotReady / onShotFailed.
   */
  captureShot(
    shotIndex: number,
    webcamBase64 = '',
  ): Promise<{ filePath?: string; base64?: string; source?: string; pending?: boolean }> {
    return ipc.invokeStrict('capture_shot', { shotIndex, base64: webcamBase64 }, 30000);
  },

  /** Nhấn nửa cò để lấy nét trước; tấm chụp ngay sau đó nhả màn trập không phải chờ. */
  prefocus(): void {
    ipc.invoke('camera_prefocus').catch(() => undefined);
  },

  /** Ảnh của một tấm đã chụp vừa tải về xong. Trả về hàm huỷ. */
  onShotReady(cb: (shot: ShotReady) => void): () => void {
    return ipc.listen('shot_ready', (data: ShotReady) => {
      if (data && typeof data.shotIndex === 'number') cb(data);
    });
  },

  /**
   * Chế độ chụp bằng điều khiển từ xa: bật thì host nhận ảnh do nút chụp / điều khiển từ xa của máy ảnh chụp
   * (máy để màn hình sau hiện live view và mở khoá nút). Tắt khi rời bước chụp.
   */
  setRemoteShutter(active: boolean): Promise<unknown> {
    return ipc.invoke('remote_capture', { active }).catch(() => undefined);
  },

  /** Ảnh do nút / điều khiển từ xa của máy ảnh vừa chụp và tải về xong (số tấm do host cấp). Trả về hàm huỷ. */
  onRemoteShot(cb: (shot: ShotReady) => void): () => void {
    return ipc.listen('remote_shot', (data: ShotReady) => {
      if (data && typeof data.shotIndex === 'number') cb(data);
    });
  },

  /** Một tấm đã chụp nhưng không tải được ảnh về. Trả về hàm huỷ. */
  onShotFailed(cb: (shot: ShotFailed) => void): () => void {
    return ipc.listen('shot_failed', (data: ShotFailed) => {
      if (data && typeof data.shotIndex === 'number') cb(data);
    });
  },

  async getStatus(): Promise<CameraStatus> {
    try {
      return normalize(await ipc.invoke('camera_status'));
    } catch {
      return OFFLINE;
    }
  },

  async reconnect(): Promise<CameraStatus> {
    try {
      return normalize(await ipc.invoke('camera_reconnect'));
    } catch {
      return OFFLINE;
    }
  },

  async startLiveView(): Promise<CameraStatus> {
    try {
      return normalize(await ipc.invoke('camera_start_liveview'));
    } catch {
      return OFFLINE;
    }
  },

  stopLiveView(): Promise<unknown> {
    return ipc.invoke('camera_stop_liveview').catch(() => undefined);
  },

  /** Chụp thử bằng máy ảnh (admin). Lỗi thì ném Error với thông báo của host. */
  testShot(): Promise<CameraTestShot> {
    return ipc.invokeStrict<CameraTestShot>('camera_test_shot', undefined, 30000);
  },

  /** Lắng nghe thay đổi trạng thái (cắm / rút máy...). Trả về hàm huỷ. */
  onStatusChanged(cb: (status: CameraStatus) => void): () => void {
    return ipc.listen('camera_status_changed', (data: unknown) => cb(normalize(data)));
  },
};
