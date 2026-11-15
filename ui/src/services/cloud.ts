import { ipc } from './ipc';

/** Tình trạng hàng đợi gửi phiên chụp lên server (host báo về). Khoá API không bao giờ được gửi về UI. */
export interface CloudStatus {
  enabled: boolean;
  configured: boolean;
  baseUrl: string;
  deviceId: string;
  effectiveDeviceId: string;
  hasApiKey: boolean;
  shareBaseUrl: string;
  maxUploadKBps: number;
  retentionHours: number;
  pendingSessions: number;
  pendingFiles: number;
  readySessions: number;
  failedSessions: number;
  oldestPendingMinutes: number | null;
  /** Có phiên chờ quá ~1 giờ hoặc bị server từ chối. */
  warning: boolean;
  warningMessage: string | null;
  lastError: string | null;
  lastErrorAt: string | null;
  lastSuccessAt: string | null;
  activity: string;
  pausedForCapture: boolean;
}

export interface CloudConfigInput {
  enabled?: boolean;
  baseUrl?: string;
  /** Bỏ trống = giữ khoá cũ. */
  apiKey?: string;
  deviceId?: string;
  maxUploadKBps?: number;
}

function normalize(raw: unknown): CloudStatus | null {
  if (!raw || typeof raw !== 'object' || !('pendingSessions' in raw)) return null;
  return raw as CloudStatus;
}

export const cloudApi = {
  async getStatus(): Promise<CloudStatus | null> {
    try {
      return normalize(await ipc.invoke('cloud_status'));
    } catch {
      return null;
    }
  },

  async configure(input: CloudConfigInput): Promise<CloudStatus | null> {
    return normalize(await ipc.invokeStrict('cloud_configure', input));
  },

  async retry(): Promise<CloudStatus | null> {
    try {
      return normalize(await ipc.invoke('cloud_retry'));
    } catch {
      return null;
    }
  },
};
