import React, { useState, useEffect, useRef } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Printer, CheckCircle, Home } from 'lucide-react';
import { ipc } from '../../services/ipc';
import type { FrameOption, FilterOption } from '../../services/backendApi';
import type { PhotoTransform } from '../../services/photoTransform';

interface FavoriPrintScreenProps {
  layoutType: string;
  frameTheme: string;
  frameObj?: FrameOption;
  filterId: string;
  filterObj?: FilterOption;
  beautifyEnabled: boolean;
  copies: number;
  photos: string[];
  /** Ảnh gốc trên máy cùng thứ tự với photos; có thì host ghép từ ảnh gốc thay cho bản xem trước. */
  photoPaths?: string[];
  /** Phóng to / độ lệch của ảnh trong từng ô, cùng thứ tự với photos. */
  transforms?: PhotoTransform[];
  onFinish: () => void;
}

interface SessionProgress {
  cloudActive: boolean;
  state: 'none' | 'pending' | 'ready' | 'failed';
  uploadedFiles: number;
  skippedFiles: number;
  totalFiles: number;
  videoEncoding: boolean;
  videoUrl: string;
}

type Status = 'composing' | 'printing' | 'uploading' | 'completed';

/** Màn hình QR ở lại 120 giây cho khách kịp quét rồi mới về trang chủ. */
const QR_SCREEN_SEC = 120;
/** Không có QR (chưa cấu hình gửi ảnh): về trang chủ sau vài giây. */
const NO_QR_SCREEN_SEC = 10;
const PRINT_ERROR_SCREEN_SEC = 30;
/**
 * Mất mạng thì không chờ mãi (khách sau đang đợi): quá thời gian này vẫn hiện QR,
 * hàng đợi tiếp tục gửi nền và link sẽ có ảnh khi gửi xong.
 */
const MAX_UPLOAD_WAIT_MS = 180_000;
const POLL_MS = 1500;
// Ghép ảnh gốc 24MP và gửi lệnh in có thể lâu hơn mức chờ 8s mặc định của ipc.invoke
const COMPOSE_TIMEOUT_MS = 120_000;
const PRINT_TIMEOUT_MS = 90_000;

export const FavoriPrintScreen: React.FC<FavoriPrintScreenProps> = ({
  layoutType,
  frameTheme,
  frameObj,
  filterId,
  filterObj,
  beautifyEnabled,
  copies,
  photos,
  photoPaths = [],
  transforms = [],
  onFinish,
}) => {
  const [status, setStatus] = useState<Status>('composing');
  const [previewImage, setPreviewImage] = useState<string | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);
  // QR tải ảnh (host dựng sẵn, không cần mạng); trống khi chưa cấu hình gửi ảnh lên server
  const [qrDataUrl, setQrDataUrl] = useState<string>('');
  const [retentionHours, setRetentionHours] = useState<number>(24);
  const [progress, setProgress] = useState<SessionProgress | null>(null);
  const [videoUrl, setVideoUrl] = useState<string>('');
  const [doneLeft, setDoneLeft] = useState<number | null>(null);
  const onFinishRef = useRef(onFinish);
  onFinishRef.current = onFinish;

  // Theo dõi video phiên (để xem trước) và tiến độ gửi lên server trong suốt lúc chờ
  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const p = await ipc.invokeStrict<SessionProgress>('session_progress', undefined, 5000);
        if (!active) return;
        if (p) {
          setProgress(p);
          if (p.videoUrl) setVideoUrl(p.videoUrl);
        }
      } catch {
        // host bận: lượt sau hỏi lại
      }
      if (active) timer = setTimeout(poll, POLL_MS);
    };
    poll();
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, []);

  // Ghép ảnh → in → chờ gửi xong
  useEffect(() => {
    let active = true;

    const processJob = async () => {
      let hasQr = false;
      try {
        const composeRes = await ipc.invokeStrict<{
          compositePath?: string;
          previewBase64?: string;
          qrUrl?: string;
          qrDataUrl?: string;
          retentionHours?: number;
        }>(
          'compose_strip',
          {
            layoutType,
            frameTheme,
            frameUrl: frameObj?.frameUrl || '',
            holes: frameObj?.holes || [],
            filterId,
            filterCode: filterObj?.code || filterId,
            beautify: beautifyEnabled,
            photos: photos.map((p, i) => photoPaths[i] || p),
            transforms,
            // Thông tin phiên gửi lên server + vị trí in QR trên frame
            frameId: frameObj?.id,
            layoutId: frameObj?.layoutId || layoutType,
            copies,
            qrX: frameObj?.qrX ?? null,
            qrY: frameObj?.qrY ?? null,
            qrSize: frameObj?.qrSize ?? null,
          },
          COMPOSE_TIMEOUT_MS,
        );

        if (!active) return;
        if (composeRes?.previewBase64) {
          setPreviewImage(`data:image/jpeg;base64,${composeRes.previewBase64}`);
        }
        hasQr = !!composeRes?.qrDataUrl;
        if (hasQr) setQrDataUrl(composeRes!.qrDataUrl!);
        if (composeRes?.retentionHours) setRetentionHours(composeRes.retentionHours);

        setStatus('printing');
        const printRes = await ipc.invokeStrict<{ success?: boolean; message?: string }>(
          'print_session',
          { copies },
          PRINT_TIMEOUT_MS,
        );
        if (!active) return;
        // Máy in từ chối / chưa sẵn sàng: báo rõ để khách gọi nhân viên, ảnh vẫn đã lưu trong máy
        if (printRes?.success === false) setPrintError(printRes?.message || 'Máy in chưa sẵn sàng');
      } catch (err) {
        console.warn('Compose/print error:', err);
        if (!active) return;
        setPrintError(err instanceof Error ? err.message : String(err));
      }

      if (!active) return;
      if (hasQr) {
        setStatus('uploading');
      } else {
        setDoneLeft(NO_QR_SCREEN_SEC);
        setStatus('completed');
      }
    };

    processJob();
    return () => {
      active = false;
    };
  }, []);

  // Đang chờ gửi: chỉ hiện QR khi server đã nhận đủ file (kể cả video)
  useEffect(() => {
    if (status !== 'uploading') return;
    const finished =
      !!progress &&
      (!progress.cloudActive ||
        ((progress.state === 'ready' || progress.state === 'failed') && !progress.videoEncoding));
    if (finished) {
      setDoneLeft(QR_SCREEN_SEC);
      setStatus('completed');
    }
  }, [status, progress]);

  // Mất mạng: quá MAX_UPLOAD_WAIT_MS kể từ lúc bắt đầu chờ thì vẫn hiện QR, hàng đợi tiếp tục gửi nền.
  // Effect riêng chỉ phụ thuộc status để đồng hồ không bị đặt lại mỗi lần cập nhật tiến độ.
  useEffect(() => {
    if (status !== 'uploading') return;
    const timer = setTimeout(() => {
      setDoneLeft(QR_SCREEN_SEC);
      setStatus((s) => (s === 'uploading' ? 'completed' : s));
    }, MAX_UPLOAD_WAIT_MS);
    return () => clearTimeout(timer);
  }, [status]);

  // Đếm ngược về trang chủ sau khi xong
  useEffect(() => {
    if (status !== 'completed' || doneLeft === null) return;
    if (doneLeft <= 0) {
      onFinishRef.current();
      return;
    }
    const timer = setTimeout(() => setDoneLeft((t) => (t === null ? t : t - 1)), 1000);
    return () => clearTimeout(timer);
  }, [status, doneLeft]);

  // Có lỗi in thì giữ màn hình lâu hơn cho khách gọi nhân viên
  useEffect(() => {
    if (status === 'completed' && printError && !qrDataUrl) setDoneLeft(PRINT_ERROR_SCREEN_SEC);
  }, [status, printError, qrDataUrl]);

  const percent = (() => {
    if (status === 'composing') return 15;
    if (status === 'printing') return 35;
    if (status === 'completed') return 100;
    if (!progress || progress.totalFiles === 0) return 40;
    const done = progress.uploadedFiles + progress.skippedFiles;
    return Math.min(98, 40 + Math.round((done / progress.totalFiles) * 58));
  })();

  // Khách chỉ thấy "đang chuẩn bị in", không nhắc tới việc gửi ảnh lên server
  const waitingTitle = 'Đang chuẩn bị in ảnh, bạn chờ xíu nhé...';

  const homeButton = (
    <button
      onClick={() => onFinishRef.current()}
      className="px-8 py-3 rounded-full bg-[#7a0000] text-white font-bold text-base active:scale-95 transition flex items-center gap-2 shadow-md"
    >
      <Home className="w-5 h-5" />
      Về trang chủ{doneLeft !== null ? ` (${doneLeft}s)` : ''}
    </button>
  );

  if (status !== 'completed') {
    return (
      <FavoriLayout stepNumber={10} hideHeader={false}>
        <div className="flex flex-col items-center justify-center gap-5 w-full max-w-4xl mx-auto my-auto py-2">
          {/* Video quá trình vừa chụp, đặt giữa màn hình */}
          <div className="flex items-center justify-center h-[52vh] w-full">
            {videoUrl ? (
              <video
                key={videoUrl}
                src={videoUrl}
                autoPlay
                loop
                muted
                playsInline
                className="h-full w-auto max-w-full object-contain bg-black shadow-xl select-none pointer-events-none"
              />
            ) : previewImage ? (
              <img
                src={previewImage}
                alt="Ảnh thành quả"
                className="h-full w-auto object-contain select-none pointer-events-none"
              />
            ) : (
              <div className="flex flex-col items-center space-y-2 p-4 text-center">
                <div className="w-8 h-8 border-3 border-[#7a0000] border-t-transparent rounded-full animate-spin" />
                <span className="text-xs text-neutral-500 font-medium">Đang chuẩn bị video phiên chụp...</span>
              </div>
            )}
          </div>

          <div className="flex flex-col items-center text-center space-y-4 w-full max-w-xl">
            <div className="flex items-center gap-3">
              <Printer className="w-8 h-8 text-[#7a0000] animate-pulse" />
              <h2 className="font-lobster text-2xl md:text-3xl text-[#1f1610] tracking-wide leading-relaxed">
                {waitingTitle}
              </h2>
            </div>

            <div className="w-full bg-[#ede4d8] h-3 rounded-full overflow-hidden border border-[#d6c4b2] shadow-inner">
              <div
                className="bg-[#7a0000] h-full transition-all duration-700 ease-out"
                style={{ width: `${percent}%` }}
              />
            </div>

            <p className="text-xs text-[#6b5b52]">Số lượng in: {copies} bản</p>
          </div>
        </div>
      </FavoriLayout>
    );
  }

  return (
    <FavoriLayout stepNumber={10} hideHeader={false}>
      <div className="flex flex-col md:flex-row items-center justify-center gap-10 w-full max-w-5xl mx-auto my-auto py-4">
        {/* Ảnh thành quả, không khung bao */}
        {previewImage && (
          <div className="flex items-center justify-center h-[62vh]">
            <img
              src={previewImage}
              alt="Ảnh thành quả"
              className="h-full w-auto object-contain select-none pointer-events-none"
            />
          </div>
        )}

        <div className="flex flex-col items-center justify-center text-center space-y-5 max-w-md">
          {printError ? (
            <div className="space-y-3 flex flex-col items-center">
              <div className="w-16 h-16 rounded-full bg-rose-100 border-2 border-rose-400 flex items-center justify-center text-[#7a0000] shadow-md">
                <Printer className="w-8 h-8" />
              </div>
              <h2 className="font-lobster text-3xl text-[#1f1610] tracking-wide">Chưa in được ảnh</h2>
              <p className="text-base text-[#7a0000] leading-relaxed whitespace-pre-line">{printError}</p>
              <p className="font-lobster text-xl text-[#1f1610] leading-relaxed">
                Ảnh của bạn đã được lưu lại.
                <br />
                Vui lòng gọi nhân viên để được in lại nhé!
              </p>
            </div>
          ) : (
            <div className="space-y-3 flex flex-col items-center">
              <div className="w-16 h-16 rounded-full bg-emerald-100 border-2 border-emerald-400 flex items-center justify-center text-emerald-700 shadow-md">
                <CheckCircle className="w-8 h-8" />
              </div>
              <h2 className="font-lobster text-3xl text-[#1f1610] tracking-wide">In ảnh thành công!</h2>
              <p className="font-lobster text-xl text-[#7a0000] leading-relaxed">
                Cảm ơn bạn đã chụp ảnh tại Favori,
                <br />
                Hãy nhận ảnh tại khay máy in nhé!
              </p>
            </div>
          )}

          {qrDataUrl && (
            <div className="flex flex-col items-center gap-3">
              <img
                src={qrDataUrl}
                alt="Mã QR tải ảnh"
                className="w-56 h-56 bg-white p-2 select-none pointer-events-none"
              />
              <p className="font-lobster text-xl text-[#1f1610] leading-snug">Quét mã để tải ảnh và video</p>
              <p className="text-xs text-[#6b5b52] leading-relaxed max-w-xs">
                Ảnh được lưu {retentionHours} giờ để bạn tải về, sau đó tự động xoá.
              </p>
            </div>
          )}

          {homeButton}
        </div>
      </div>
    </FavoriLayout>
  );
};
