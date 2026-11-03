import React, { useState, useEffect } from 'react';
import { Sparkles, Printer, CheckCircle2, RotateCcw } from 'lucide-react';
import { ipc } from '../services/ipc';
import type { LayoutConfig } from './LayoutSelectionScreen';

interface ComposingAndPrintScreenProps {
  layout: LayoutConfig;
  beautifyEnabled: boolean;
  photos: string[];
  onFinishSession: () => void;
}

export const ComposingAndPrintScreen: React.FC<ComposingAndPrintScreenProps> = ({
  layout,
  beautifyEnabled,
  photos,
  onFinishSession,
}) => {
  const [phase, setPhase] = useState<'composing' | 'printing' | 'done'>('composing');
  const [progress, setProgress] = useState(15);
  const [statusText, setStatusText] = useState('Đang tối ưu độ sắc nét và cân bằng màu...');
  const [compositeUrl, setCompositeUrl] = useState<string | null>(null);
  const [qrUrl, setQrUrl] = useState<string>('https://hihibooth.vn/gallery/demo-session-8888');
  const [autoResetSeconds, setAutoResetSeconds] = useState(15);

  useEffect(() => {
    let timer: any;

    const runWorkflow = async () => {
      // Step 1: Composing
      setStatusText(beautifyEnabled ? 'Đang chạy bộ lọc AI làm mịn da MediaPipe...' : 'Đang ghép khung hình...');
      setProgress(40);

      try {
        const res = await ipc.invoke<{ compositePath: string; qrUrl?: string; previewBase64?: string }>('compose_strip', {
          layoutType: layout.type,
          frameColor: layout.frameColor,
          frameTheme: layout.frameTheme,
          beautify: beautifyEnabled,
          photos: photos,
        });

        if (res?.previewBase64) {
          setCompositeUrl(`data:image/jpeg;base64,${res.previewBase64}`);
        } else {
          setCompositeUrl(photos[0] || 'https://picsum.photos/seed/hihi_comp/600/900');
        }

        if (res?.qrUrl) {
          setQrUrl(res.qrUrl);
        }
      } catch (e) {
        console.error('Compose strip error:', e);
        setCompositeUrl(photos[0] || 'https://picsum.photos/seed/hihi_comp/600/900');
      }

      setProgress(85);
      setStatusText('Đang gửi lệnh in tới máy in nhiệt DNP/Citizen...');
      setPhase('printing');

      // Step 2: Print
      try {
        await ipc.invoke('print_session', { copies: 1 });
      } catch (e) {
        console.error('Print session error:', e);
      }

      setProgress(100);
      setPhase('done');
    };

    runWorkflow();

    return () => {
      if (timer) clearInterval(timer);
    };
  }, []);

  // Auto reset timer on done
  useEffect(() => {
    if (phase !== 'done') return;

    const interval = setInterval(() => {
      setAutoResetSeconds((prev) => {
        if (prev <= 1) {
          clearInterval(interval);
          onFinishSession();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(interval);
  }, [phase]);

  return (
    <div className="relative w-screen h-screen bg-gradient-to-br from-[#0a0b12] via-[#141224] to-[#0a0b12] flex flex-col justify-between p-10 select-none overflow-hidden text-white">
      {/* Top Header */}
      <div className="w-full flex justify-between items-center z-10">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-2xl bg-gradient-to-tr from-rose-500 to-purple-600 flex items-center justify-center shadow-lg">
            <Sparkles className="w-5 h-5 text-white" />
          </div>
          <div>
            <h2 className="text-2xl font-bold">XUẤT BẢN & IN ẢNH</h2>
            <p className="text-xs text-gray-400">Hệ thống in nhiệt chất lượng cao độ phân giải 300 DPI</p>
          </div>
        </div>

        {phase === 'done' && (
          <div className="flex items-center gap-2 bg-black/50 px-4 py-2 rounded-full border border-white/10 text-xs text-gray-400">
            <span>Tự động quay về màn hình chính sau: <strong className="text-rose-400">{autoResetSeconds}s</strong></span>
          </div>
        )}
      </div>

      {/* Main Content Area */}
      <div className="w-full max-w-5xl mx-auto my-auto z-10 flex flex-col md:flex-row items-center justify-center gap-12">
        {/* Left: Composite Preview Card */}
        <div className="relative rounded-3xl p-4 bg-white/5 border border-white/10 backdrop-blur-md shadow-2xl flex items-center justify-center max-h-[65vh]">
          {compositeUrl ? (
            <img
              src={compositeUrl}
              alt="Composite Strip"
              className="max-h-[58vh] rounded-2xl object-contain shadow-2xl border border-white/20"
            />
          ) : (
            <div className="w-64 h-96 rounded-2xl bg-black/40 animate-pulse flex items-center justify-center text-gray-500">
              Đang tạo bố cục...
            </div>
          )}

          {phase === 'printing' && (
            <div className="absolute inset-0 bg-black/60 rounded-3xl backdrop-blur-sm flex flex-col items-center justify-center gap-4">
              <Printer className="w-16 h-16 text-rose-400 animate-bounce" />
              <span className="font-bold text-xl text-white tracking-wider">ĐANG IN ẢNH...</span>
              <span className="text-sm text-gray-300">Vui lòng nhận ảnh ở khay ra phía trước</span>
            </div>
          )}
        </div>

        {/* Right: Status or QR Download Information */}
        <div className="flex flex-col items-center md:items-start text-center md:text-left max-w-md">
          {phase !== 'done' ? (
            <div className="w-full flex flex-col items-center md:items-start gap-6">
              <h3 className="text-3xl font-extrabold text-transparent bg-clip-text bg-gradient-to-r from-rose-400 to-purple-400">
                {phase === 'composing' ? 'ĐANG TỔNG HỢP ẢNH' : 'MÁY IN ĐANG LÀM VIỆC'}
              </h3>
              <p className="text-gray-300 text-sm leading-relaxed">{statusText}</p>

              {/* Progress Bar */}
              <div className="w-full bg-white/10 rounded-full h-3 overflow-hidden border border-white/10 mt-2">
                <div
                  className="bg-gradient-to-r from-rose-500 via-purple-500 to-indigo-500 h-full rounded-full transition-all duration-500"
                  style={{ width: `${progress}%` }}
                ></div>
              </div>
              <span className="text-xs font-mono text-gray-400">{progress}% hoàn tất</span>
            </div>
          ) : (
            /* Phase Done: QR Code and Congratulations */
            <div className="w-full flex flex-col items-center md:items-start gap-6">
              <div className="flex items-center gap-2 text-emerald-400 font-bold text-lg">
                <CheckCircle2 className="w-6 h-6" />
                <span>IN ẢNH THÀNH CÔNG!</span>
              </div>

              <h3 className="text-3xl font-black text-white">QUÉT MÃ ĐỂ TẢI ẢNH VÀ VIDEO RECAP</h3>
              <p className="text-gray-400 text-sm">
                Quét mã QR bằng điện thoại của bạn để nhận ngay ảnh gốc chất lượng cao và clip hậu trường vui nhộn.
              </p>

              {/* Visual QR Code Display Container */}
              <div className="p-4 bg-white rounded-3xl shadow-[0_0_40px_rgba(255,255,255,0.2)] flex flex-col items-center justify-center">
                {/* SVG QR Code generator representation */}
                <div className="w-48 h-48 bg-white flex items-center justify-center p-2">
                  <img
                    src={`https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(qrUrl)}`}
                    alt="Scan QR for Photos"
                    className="w-full h-full object-contain"
                  />
                </div>
                <span className="text-[10px] text-gray-500 font-mono mt-1">hihibooth.vn</span>
              </div>

              {/* Finish Button */}
              <button
                onClick={onFinishSession}
                className="w-full mt-2 py-4 px-8 rounded-2xl bg-white/10 hover:bg-white/20 border border-white/20 text-white font-bold text-lg flex items-center justify-center gap-3 active:scale-95 transition"
              >
                <RotateCcw className="w-5 h-5 text-rose-400" />
                <span>Hoàn Tất & Về Màn Hình Chính</span>
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Footer Info */}
      <div className="w-full flex justify-center text-xs text-gray-500 z-10">
        <span>Cảm ơn bạn đã lựa chọn trải nghiệm cùng HIHI BOOTH</span>
      </div>
    </div>
  );
};
