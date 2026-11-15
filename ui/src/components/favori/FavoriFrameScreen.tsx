import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2, Image as ImageIcon } from 'lucide-react';
import { backendApi, type FrameOption, type LayoutOption } from '../../services/backendApi';

export type { FrameOption };

interface FavoriFrameScreenProps {
  layout?: LayoutOption;
  selectedFrame: string;
  onSelectFrame: (id: string, frameObj?: FrameOption) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriFrameScreen: React.FC<FavoriFrameScreenProps> = ({
  layout,
  selectedFrame,
  onSelectFrame,
  onNext,
  onBack,
}) => {
  const [frames, setFrames] = useState<FrameOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  const layoutId = layout?.id;

  useEffect(() => {
    let isMounted = true;
    setLoading(true);

    backendApi.getFrames(layoutId).then((data) => {
      if (!isMounted) return;
      setFrames(data);
      setLoading(false);

      // Frame đang giữ không thuộc layout này (đổi layout, phiên trước): chọn lại frame đầu tiên
      const matched = data.find((f) => f.id === selectedFrame);
      const next = matched || data[0];
      if (next) onSelectFrame(next.id, next);
    });

    return () => {
      isMounted = false;
    };
  }, [layoutId]);

  const hasSelection = frames.some((f) => f.id === selectedFrame);
  const aspectRatio = layout ? `${layout.widthPx} / ${layout.heightPx}` : '2 / 3';

  return (
    <FavoriLayout
      stepNumber={2}
      stepTitle="Chọn khung"
      onBack={onBack}
      onNext={onNext}
      disableNext={!hasSelection}
      wide
    >
      {loading ? (
        <div className="flex flex-col items-center justify-center py-24 space-y-3">
          <Loader2 className="w-10 h-10 animate-spin text-[#7a0000]" />
          <span className="text-[#6b5b52] font-medium text-base">Đang tải danh sách khung...</span>
        </div>
      ) : frames.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 space-y-3">
          <ImageIcon className="w-12 h-12 text-neutral-400" />
          <span className="text-[#6b5b52] font-medium text-base">
            Chưa có khung cho kích cỡ này. Vui lòng quay về chọn kích cỡ khác.
          </span>
        </div>
      ) : (
        <div className="w-full h-full overflow-y-auto px-6 py-4">
          <div className="flex flex-wrap items-start justify-center gap-x-8 gap-y-6 max-w-[1400px] mx-auto">
            {frames.map((frame) => {
              const isSelected = selectedFrame === frame.id;
              return (
                <button
                  key={frame.id}
                  type="button"
                  onClick={() => onSelectFrame(frame.id, frame)}
                  aria-pressed={isSelected}
                  aria-label={frame.name}
                  style={{ aspectRatio }}
                  className={`h-[34vh] min-h-[220px] p-0 border-0 bg-transparent cursor-pointer transition-all duration-200 ${
                    isSelected
                      ? 'scale-[1.06] drop-shadow-xl'
                      : 'opacity-80 active:scale-[0.98]'
                  }`}
                >
                  <img
                    src={frame.thumbnailUrl || frame.frameUrl}
                    alt=""
                    className="w-full h-full object-contain pointer-events-none select-none"
                    loading="lazy"
                    onError={(e) => {
                      const img = e.currentTarget;
                      if (frame.frameUrl && img.src !== frame.frameUrl) {
                        img.src = frame.frameUrl;
                      }
                    }}
                  />
                </button>
              );
            })}
          </div>
        </div>
      )}
    </FavoriLayout>
  );
};
