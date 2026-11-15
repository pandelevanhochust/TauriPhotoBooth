import React, { useEffect, useRef, useState } from 'react';
import { Camera, RefreshCw, Video, VideoOff, Aperture, Loader2 } from 'lucide-react';
import { ipc } from '../../services/ipc';
import { cameraApi, type CameraStatus, type CameraTestShot } from '../../services/camera';

function batteryLabel(level: number | null): string | null {
  if (level === null || level === undefined) return null;
  if (level < 0) return 'Nguồn ngoài';
  return `${level}%`;
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '';
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
}

/** Kiểm tra kết nối máy ảnh Canon (EDSDK): trạng thái, kết nối lại, xem trực tiếp, chụp thử. */
export const CameraCheckPanel: React.FC = () => {
  const [status, setStatus] = useState<CameraStatus | null>(null);
  const [checking, setChecking] = useState(false);
  const [liveOn, setLiveOn] = useState(false);
  const [liveFrame, setLiveFrame] = useState<string | null>(null);
  const [shooting, setShooting] = useState(false);
  const [testShot, setTestShot] = useState<CameraTestShot | null>(null);
  const [shotError, setShotError] = useState<string | null>(null);
  const liveOnRef = useRef(false);
  liveOnRef.current = liveOn;

  useEffect(() => {
    let active = true;
    cameraApi.getStatus().then((s) => active && setStatus(s));
    const unlisten = cameraApi.onStatusChanged((s) => {
      setStatus(s);
      if (!s.connected) {
        setLiveOn(false);
        setLiveFrame(null);
      }
    });
    return () => {
      active = false;
      unlisten();
      if (liveOnRef.current) cameraApi.stopLiveView();
    };
  }, []);

  useEffect(() => {
    if (!liveOn) return;
    const unlisten = ipc.listen<{ base64: string }>('camera_frame', (data) => {
      if (data?.base64) setLiveFrame(`data:image/jpeg;base64,${data.base64}`);
    });
    cameraApi.startLiveView().then(setStatus);
    return () => {
      unlisten();
      cameraApi.stopLiveView();
      setLiveFrame(null);
    };
  }, [liveOn]);

  const handleCheck = async () => {
    setChecking(true);
    setShotError(null);
    setLiveOn(false);
    try {
      setStatus(await cameraApi.reconnect());
    } finally {
      setChecking(false);
    }
  };

  const handleTestShot = async () => {
    setShooting(true);
    setShotError(null);
    setTestShot(null);
    try {
      const res = await cameraApi.testShot();
      if (!res?.previewBase64) throw new Error('Host không trả ảnh về');
      setTestShot(res);
    } catch (e) {
      setShotError(e instanceof Error ? e.message : String(e));
    } finally {
      setShooting(false);
    }
  };

  const connected = !!status?.connected;
  const sdkMissing = status !== null && !status.sdkAvailable;
  const dotClass = status === null ? 'bg-gray-500' : connected ? 'bg-emerald-400' : sdkMissing ? 'bg-amber-400' : 'bg-rose-500';
  const battery = batteryLabel(status?.batteryLevel ?? null);

  const actionBtn =
    'px-4 py-2 rounded-xl text-xs font-bold transition active:scale-95 flex items-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed';

  return (
    <div className="p-4 rounded-2xl bg-white/5 border border-white/10 space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-3 min-w-0">
          <Camera className="w-5 h-5 text-purple-400 mt-0.5 flex-shrink-0" />
          <div className="min-w-0">
            <div className="font-semibold text-sm flex items-center gap-2">
              <span className={`inline-block w-2.5 h-2.5 rounded-full ${dotClass}`} />
              {status === null
                ? 'Đang kiểm tra máy ảnh...'
                : connected
                  ? `Đã kết nối: ${[status.vendor, status.model].filter(Boolean).join(' ') || 'máy ảnh'}`
                  : 'Chưa kết nối máy ảnh (Canon / Fujifilm)'}
            </div>
            {status && (
              <div className="text-xs text-gray-400 mt-1 space-y-0.5">
                {!connected && <div className="text-gray-300 whitespace-pre-line">{status.message}</div>}
                {connected && (
                  <div>
                    {status.liveView ? 'Đang xem trực tiếp' : 'Sẵn sàng chụp'}
                    {battery ? ` · Pin: ${battery}` : ''}
                  </div>
                )}
                {!connected && !sdkMissing && (
                  <div>Khi chưa có máy ảnh, bước chụp sẽ dùng webcam của máy tính.</div>
                )}
              </div>
            )}
          </div>
        </div>
        <button
          onClick={handleCheck}
          disabled={checking}
          className={`${actionBtn} bg-white/10 hover:bg-white/20 flex-shrink-0`}
        >
          {checking ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCw className="w-4 h-4" />}
          Kiểm tra lại
        </button>
      </div>

      {connected && (
        <div className="flex gap-3">
          <button
            onClick={() => setLiveOn((v) => !v)}
            className={`${actionBtn} ${liveOn ? 'bg-purple-500/30 text-purple-200' : 'bg-white/10 hover:bg-white/20'}`}
          >
            {liveOn ? <VideoOff className="w-4 h-4" /> : <Video className="w-4 h-4" />}
            {liveOn ? 'Tắt xem trực tiếp' : 'Xem trực tiếp'}
          </button>
          <button
            onClick={handleTestShot}
            disabled={shooting}
            className={`${actionBtn} bg-rose-500 hover:bg-rose-600`}
          >
            {shooting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Aperture className="w-4 h-4" />}
            Chụp thử
          </button>
        </div>
      )}

      {liveOn && (
        <div className="aspect-[3/2] w-full max-w-md bg-black rounded-xl overflow-hidden flex items-center justify-center">
          {liveFrame ? (
            <img src={liveFrame} alt="Xem trực tiếp" className="w-full h-full object-contain" />
          ) : (
            <span className="text-xs text-gray-400">Đang chờ hình từ máy ảnh...</span>
          )}
        </div>
      )}

      {shotError && (
        <div className="text-xs text-rose-300 bg-rose-500/10 border border-rose-500/30 rounded-xl px-3 py-2">
          Chụp thử thất bại: {shotError}
        </div>
      )}

      {testShot && (
        <div className="flex gap-4 items-start">
          <img
            src={`data:image/jpeg;base64,${testShot.previewBase64}`}
            alt="Ảnh chụp thử"
            className="w-56 rounded-lg border border-white/10"
          />
          <div className="text-xs text-gray-300 space-y-1 min-w-0">
            <div className="text-emerald-400 font-semibold">Chụp thử thành công</div>
            {testShot.resolution && <div>Độ phân giải: {testShot.resolution}</div>}
            {testShot.fileSize > 0 && <div>Dung lượng: {formatBytes(testShot.fileSize)}</div>}
            <div className="text-gray-500 break-all">{testShot.filePath}</div>
          </div>
        </div>
      )}
    </div>
  );
};
