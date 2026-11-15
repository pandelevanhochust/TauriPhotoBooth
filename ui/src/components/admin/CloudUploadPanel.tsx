import React, { useEffect, useState } from 'react';
import { CloudUpload, RefreshCw, Loader2, Save, AlertTriangle } from 'lucide-react';
import { cloudApi, type CloudStatus } from '../../services/cloud';

function formatMinutes(minutes: number): string {
  if (minutes >= 60) return `${Math.floor(minutes / 60)} giờ ${Math.floor(minutes % 60)} phút`;
  return `${Math.max(0, Math.floor(minutes))} phút`;
}

function formatTime(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleTimeString('vi-VN');
}

/** Gửi ảnh lên server: cấu hình (server, khoá API, mã máy) và tình trạng hàng đợi cho người vận hành. */
export const CloudUploadPanel: React.FC = () => {
  const [status, setStatus] = useState<CloudStatus | null>(null);
  const [baseUrl, setBaseUrl] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [deviceId, setDeviceId] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const load = async (fillForm: boolean) => {
    const s = await cloudApi.getStatus();
    setStatus(s);
    if (s && fillForm) {
      setBaseUrl(s.baseUrl);
      setDeviceId(s.deviceId);
      setEnabled(s.enabled);
    }
  };

  useEffect(() => {
    load(true);
    // Tự làm mới tình trạng hàng đợi mỗi 5 giây khi panel đang mở
    const timer = setInterval(() => load(false), 5000);
    return () => clearInterval(timer);
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const s = await cloudApi.configure({ enabled, baseUrl, apiKey: apiKey || undefined, deviceId });
      setStatus(s);
      setApiKey('');
      setMessage({ ok: true, text: 'Đã lưu cấu hình, hàng đợi sẽ thử gửi lại ngay.' });
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : String(e) });
    } finally {
      setSaving(false);
    }
  };

  const handleRetry = async () => {
    setStatus(await cloudApi.retry());
  };

  if (!status) {
    return (
      <div className="p-4 rounded-2xl bg-white/5 border border-white/10 text-xs text-gray-400">
        Gửi ảnh lên server: không có host (đang chạy trên trình duyệt).
      </div>
    );
  }

  const dotClass = !status.enabled
    ? 'bg-gray-500'
    : !status.configured || status.warning
      ? 'bg-amber-400'
      : status.pendingSessions > 0
        ? 'bg-sky-400'
        : 'bg-emerald-400';

  const input =
    'w-full bg-black/50 border border-white/20 rounded-xl px-3 py-2 text-xs text-white placeholder:text-gray-500';
  const actionBtn =
    'px-4 py-2 rounded-xl text-xs font-bold transition active:scale-95 flex items-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed';

  return (
    <div className="p-4 rounded-2xl bg-white/5 border border-white/10 space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-3 min-w-0">
          <CloudUpload className="w-5 h-5 text-rose-400 mt-0.5 flex-shrink-0" />
          <div className="min-w-0">
            <div className="font-semibold text-sm flex items-center gap-2">
              <span className={`inline-block w-2.5 h-2.5 rounded-full ${dotClass}`} />
              Gửi ảnh lên server (QR tải ảnh)
            </div>
            <div className="text-xs text-gray-400 mt-1 space-y-0.5">
              <div className="text-gray-300">{status.activity}</div>
              <div>
                Đang chờ: {status.pendingSessions} phiên ({status.pendingFiles} file)
                {status.oldestPendingMinutes != null && ` · phiên cũ nhất chờ ${formatMinutes(status.oldestPendingMinutes)}`}
                {' · '}Đã gửi xong: {status.readySessions}
                {status.failedSessions > 0 && ` · Bị từ chối: ${status.failedSessions}`}
              </div>
              <div>Link QR: {status.shareBaseUrl}/… · lưu {status.retentionHours} giờ</div>
              {status.lastSuccessAt && <div>Gửi thành công gần nhất: {formatTime(status.lastSuccessAt)}</div>}
            </div>
          </div>
        </div>
        <button onClick={handleRetry} className={`${actionBtn} bg-white/10 hover:bg-white/20 flex-shrink-0`}>
          <RefreshCw className="w-4 h-4" />
          Gửi lại ngay
        </button>
      </div>

      {status.warning && status.warningMessage && (
        <div className="text-xs rounded-xl px-3 py-2 border text-amber-200 bg-amber-500/10 border-amber-500/30 flex gap-2">
          <AlertTriangle className="w-4 h-4 flex-shrink-0" />
          <span>{status.warningMessage}</span>
        </div>
      )}
      {status.lastError && (
        <div className="text-[11px] text-rose-300 break-words">
          Lỗi gần nhất {formatTime(status.lastErrorAt)}: {status.lastError}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <label className="space-y-1">
          <span className="text-[11px] text-gray-400">Địa chỉ server</span>
          <input className={input} value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="https://api.favori.vn" />
        </label>
        <label className="space-y-1">
          <span className="text-[11px] text-gray-400">Khoá API {status.hasApiKey ? '(đã có, để trống = giữ nguyên)' : '(chưa có)'}</span>
          <input
            className={input}
            type="password"
            autoComplete="off"
            value={apiKey}
            onChange={(e) => setApiKey(e.target.value)}
            placeholder={status.hasApiKey ? '••••••••' : 'X-Api-Key'}
          />
        </label>
        <label className="space-y-1">
          <span className="text-[11px] text-gray-400">Mã máy (trống = {status.effectiveDeviceId})</span>
          <input className={input} value={deviceId} onChange={(e) => setDeviceId(e.target.value)} placeholder="KIOSK-01" />
        </label>
      </div>

      <div className="flex items-center justify-between gap-3">
        <label className="flex items-center gap-2 text-xs text-gray-300">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} className="w-4 h-4 accent-rose-500" />
          Bật gửi ảnh và in QR tải ảnh
        </label>
        <button onClick={handleSave} disabled={saving} className={`${actionBtn} bg-rose-500 hover:bg-rose-600`}>
          {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
          Lưu
        </button>
      </div>

      {message && (
        <div
          className={`text-xs rounded-xl px-3 py-2 border ${
            message.ok
              ? 'text-emerald-300 bg-emerald-500/10 border-emerald-500/30'
              : 'text-rose-300 bg-rose-500/10 border-rose-500/30'
          }`}
        >
          {message.text}
        </div>
      )}
    </div>
  );
};
