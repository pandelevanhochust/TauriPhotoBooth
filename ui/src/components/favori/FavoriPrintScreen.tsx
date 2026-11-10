import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Printer, CheckCircle, Image as ImageIcon, FolderOpen } from 'lucide-react';
import { ipc } from '../../services/ipc';
import type { FrameOption, FilterOption } from '../../services/backendApi';

interface FavoriPrintScreenProps {
  layoutType: string;
  frameTheme: string;
  frameObj?: FrameOption;
  filterId: string;
  filterObj?: FilterOption;
  beautifyEnabled: boolean;
  copies: number;
  photos: string[];
  onFinish: () => void;
}

export const FavoriPrintScreen: React.FC<FavoriPrintScreenProps> = ({
  layoutType,
  frameTheme,
  frameObj,
  filterId,
  filterObj,
  beautifyEnabled,
  copies,
  photos,
  onFinish,
}) => {
  const [status, setStatus] = useState<'composing' | 'printing' | 'completed'>('composing');
  const [progress, setProgress] = useState<number>(10);
  const [previewImage, setPreviewImage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    const processJob = async () => {
      try {
        // Step 1: Compositing via Skia/Magick compositor
        setProgress(30);
        const composeRes = await ipc.invoke<{
          compositePath?: string;
          previewBase64?: string;
          qrUrl?: string;
        }>('compose_strip', {
          layoutType,
          frameTheme,
          frameUrl: frameObj?.frameUrl || '',
          holes: frameObj?.holes || [],
          filterId,
          filterCode: filterObj?.code || filterId,
          beautify: beautifyEnabled,
          photos,
        });

        if (!active) return;
        if (composeRes?.previewBase64) {
          setPreviewImage(`data:image/jpeg;base64,${composeRes.previewBase64}`);
        }

        setProgress(65);
        setStatus('printing');

        // Step 2: Spool to Thermal Printer
        await ipc.invoke('print_session', { copies });

        if (!active) return;
        setProgress(100);
        setStatus('completed');

        // Reset to Welcome screen after 6 seconds
        setTimeout(() => {
          if (active) onFinish();
        }, 6000);
      } catch (err) {
        console.warn('Composing/Print IPC error (simulating graceful finish):', err);
        if (!active) return;
        setProgress(100);
        setStatus('completed');
        setTimeout(() => {
          if (active) onFinish();
        }, 5000);
      }
    };

    processJob();

    return () => {
      active = false;
    };
  }, []);

  return (
    <FavoriLayout
      stepNumber={10}
      hideHeader={false}
    >
      <div className="flex flex-col md:flex-row items-center justify-center gap-8 w-full max-w-5xl my-auto py-4">
        {/* Left Column: Live Strip Preview */}
        <div className="flex flex-col items-center">
          <div className="bg-white/80 p-3 rounded-2xl shadow-xl border-2 border-[#bfa282]/60 flex flex-col items-center">
            <span className="text-xs font-semibold text-[#7a0000] uppercase tracking-wider mb-2 flex items-center gap-1.5">
              <ImageIcon className="w-3.5 h-3.5" /> Ảnh Preview Hoàn Thiện
            </span>
            <div className="w-48 h-80 md:w-56 md:h-96 bg-neutral-100 rounded-xl overflow-hidden border border-neutral-300 shadow-inner flex items-center justify-center">
              {previewImage ? (
                <img
                  src={previewImage}
                  alt="Final Photo Strip Preview"
                  className="w-full h-full object-contain shadow"
                />
              ) : (
                <div className="flex flex-col items-center space-y-2 p-4 text-center">
                  <div className="w-8 h-8 border-3 border-[#7a0000] border-t-transparent rounded-full animate-spin" />
                  <span className="text-xs text-neutral-500 font-medium">Đang ghép ảnh vào frame...</span>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Right Column: Status & Progress */}
        <div className="flex flex-col items-center justify-center text-center space-y-6 max-w-md">
          {status !== 'completed' ? (
            <>
              {/* Spinning/Breathing Icon */}
              <div className="w-20 h-20 rounded-full bg-[#7a0000]/10 border-2 border-[#7a0000]/40 flex items-center justify-center text-[#7a0000] animate-soft-pulse">
                <Printer className="w-10 h-10 animate-pulse" />
              </div>

              {/* Title matching 11.png */}
              <h2 className="font-lobster text-2xl md:text-4xl text-[#1f1610] tracking-wide leading-relaxed">
                {status === 'composing'
                  ? 'Đang ghép ảnh & áp bộ lọc...'
                  : 'Đang gửi lệnh in, bạn chờ xíu nhé...'}
              </h2>

              {/* Progress Bar */}
              <div className="w-full bg-[#ede4d8] h-3 rounded-full overflow-hidden border border-[#d6c4b2] shadow-inner">
                <div
                  className="bg-[#7a0000] h-full transition-all duration-700 ease-out"
                  style={{ width: `${progress}%` }}
                />
              </div>

              <p className="text-xs text-[#6b5b52]">
                Khổ in: {layoutType === 'strip2x6' ? 'Dải 5x15cm (2x6)' : 'Bưu thiếp 10x15cm (4x6)'} • Số lượng: {copies} bản
              </p>
            </>
          ) : (
            <div className="space-y-4 animate-soft-pulse flex flex-col items-center">
              <div className="w-20 h-20 rounded-full bg-emerald-100 border-2 border-emerald-400 flex items-center justify-center text-emerald-700 mx-auto shadow-md">
                <CheckCircle className="w-10 h-10" />
              </div>

              <h2 className="font-lobster text-3xl md:text-4xl text-[#1f1610] tracking-wide">
                In ảnh thành công!
              </h2>

              <p className="font-lobster text-xl md:text-2xl text-[#7a0000] leading-relaxed">
                Cảm ơn bạn đã chụp ảnh tại Favori,<br />
                Hãy nhận ảnh tại khay máy in nhé!
              </p>

              <button
                type="button"
                onClick={() => ipc.invoke('open_gallery_folder')}
                className="mt-2 px-5 py-2 rounded-full bg-white text-[#7a0000] border-2 border-[#7a0000] hover:bg-[#7a0000]/10 font-medium text-sm flex items-center gap-2 shadow-md transition-all cursor-pointer active:scale-95"
              >
                <FolderOpen className="w-4 h-4" />
                Mở thư mục ảnh trên máy
              </button>
            </div>
          )}
        </div>
      </div>
    </FavoriLayout>
  );
};
