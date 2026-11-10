import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2, Image as ImageIcon } from 'lucide-react';
import { backendApi, type FrameOption } from '../../services/backendApi';

export type { FrameOption };

interface FavoriFrameScreenProps {
  selectedSize?: string;
  selectedFrame: string;
  onSelectFrame: (id: string, frameObj?: FrameOption) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriFrameScreen: React.FC<FavoriFrameScreenProps> = ({
  selectedSize,
  selectedFrame,
  onSelectFrame,
  onNext,
  onBack,
}) => {
  const [frames, setFrames] = useState<FrameOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    backendApi.getFrames(selectedSize).then((data) => {
      if (isMounted) {
        console.log('[FavoriFrameScreen] Loaded frames:', data);
        setFrames(data);
        setLoading(false);
        if (data.length > 0) {
          const matched = data.find((f) => f.id === selectedFrame);
          if (matched) {
            onSelectFrame(matched.id, matched);
          } else {
            onSelectFrame(data[0].id, data[0]);
          }
        }
      }
    });
    return () => {
      isMounted = false;
    };
  }, [selectedSize]);

  return (
    <FavoriLayout
      stepNumber={2}
      stepTitle="Chọn khung ảnh"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="flex flex-col items-center w-full max-w-5xl py-2">
        {loading ? (
          <div className="flex flex-col items-center justify-center py-20 space-y-3">
            <Loader2 className="w-10 h-10 animate-spin text-[#7a0000]" />
            <span className="text-[#6b5b52] font-medium text-base">
              Đang tải danh sách khung từ máy chủ CMS...
            </span>
          </div>
        ) : frames.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 space-y-3">
            <ImageIcon className="w-12 h-12 text-neutral-400" />
            <span className="text-[#6b5b52] font-medium text-base">
              Không tìm thấy khung ảnh phù hợp cho kích cỡ này.
            </span>
          </div>
        ) : (
          <div className="flex flex-wrap items-center justify-center gap-6 w-full py-4">
            {frames.map((frame) => {
              const isSelected = selectedFrame === frame.id;
              return (
                <div
                  key={frame.id}
                  onClick={() => onSelectFrame(frame.id, frame)}
                  className={`favori-card flex flex-col items-center p-4 cursor-pointer w-48 md:w-56 transition-all duration-200 ${
                    isSelected
                      ? 'ring-3 ring-[#7a0000] scale-105 shadow-2xl'
                      : 'hover:scale-102 hover:border-[#7a0000]/50'
                  }`}
                >
                  {/* Frame Thumbnail / Preview Image */}
                  <div
                    className="w-full h-52 rounded-xl flex items-center justify-center p-2 border border-neutral-300 shadow-inner mb-3 overflow-hidden relative bg-neutral-100"
                    style={{
                      backgroundColor: frame.bgColor || '#ffffff',
                    }}
                  >
                    {frame.thumbnailUrl ? (
                      <img
                        src={frame.thumbnailUrl}
                        alt={frame.name}
                        className="w-full h-full object-contain pointer-events-none select-none transition-transform duration-200"
                        loading="lazy"
                        onError={(e) => {
                          // Nếu thumbnail lỗi, fallback sang frameUrl
                          if (frame.frameUrl && (e.currentTarget as HTMLImageElement).src !== frame.frameUrl) {
                            (e.currentTarget as HTMLImageElement).src = frame.frameUrl;
                          }
                        }}
                      />
                    ) : frame.frameUrl ? (
                      <img
                        src={frame.frameUrl}
                        alt={frame.name}
                        className="w-full h-full object-contain pointer-events-none select-none"
                        loading="lazy"
                      />
                    ) : (
                      /* Mockup apertures if no image */
                      <div className="w-full h-full flex flex-col items-center justify-around py-2">
                        {frame.holes && frame.holes.length > 0 ? (
                          frame.holes.slice(0, 4).map((_, idx) => (
                            <div
                              key={idx}
                              className="w-4/5 h-8 bg-neutral-200/90 rounded-sm border border-neutral-300"
                            />
                          ))
                        ) : (
                          <>
                            <div className="w-4/5 h-9 bg-neutral-200/90 rounded-sm border border-neutral-300" />
                            <div className="w-4/5 h-9 bg-neutral-200/90 rounded-sm border border-neutral-300" />
                            <div className="w-4/5 h-9 bg-neutral-200/90 rounded-sm border border-neutral-300" />
                          </>
                        )}
                        <span className="text-[10px] font-serif italic text-neutral-500">
                          Favori
                        </span>
                      </div>
                    )}

                    {/* Layout badge */}
                    <div className="absolute top-2 right-2 bg-black/60 text-white text-[10px] px-2 py-0.5 rounded-full uppercase tracking-wider backdrop-blur-sm">
                      {frame.layoutId}
                    </div>
                  </div>

                  {/* Title & selection indicator */}
                  <span className="font-lobster text-lg text-[#1f1610] text-center leading-snug line-clamp-2 min-h-[44px] flex items-center justify-center">
                    {frame.name}
                  </span>

                  <div className="mt-2">
                    <div
                      className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${
                        isSelected ? 'border-[#7a0000] bg-[#7a0000]' : 'border-[#c5a880]'
                      }`}
                    >
                      {isSelected && <div className="w-2 h-2 rounded-full bg-white" />}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </FavoriLayout>
  );
};
