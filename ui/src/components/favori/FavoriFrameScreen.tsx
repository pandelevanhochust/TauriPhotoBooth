import React, { useState, useEffect, useRef } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2, Image as ImageIcon } from 'lucide-react';
import { backendApi, type FrameOption, type LayoutOption } from '../../services/backendApi';
import { ipc } from '../../services/ipc';

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

  // Frame đang chọn, đọc trong callback của sự kiện mà không phải đăng ký lại
  const selectedRef = useRef(selectedFrame);
  selectedRef.current = selectedFrame;

  useEffect(() => {
    let isMounted = true;
    setLoading(true);

    const load = (fromServerUpdate: boolean) =>
      backendApi.getFrames(layoutId).then((data) => {
        if (!isMounted) return;
        setFrames(data);
        setLoading(false);

        const matched = data.find((f) => f.id === selectedRef.current);
        if (matched) {
          // Truyền lại bản mới (ô ảnh, vị trí QR có thể vừa đổi trên CMS)
          onSelectFrame(matched.id, matched);
        } else if (fromServerUpdate) {
          // Frame khách đang chọn vừa bị gỡ trên CMS: bỏ chọn để khách tự chọn lại (nút Tiếp tục bị khoá tới khi chọn)
          if (selectedRef.current) onSelectFrame('', undefined);
        } else if (data[0]) {
          // Mới vào bước (đổi layout, phiên trước): chọn sẵn frame đầu tiên
          onSelectFrame(data[0].id, data[0]);
        }
      });

    // Host trả ngay bản đã lưu rồi hỏi server ngầm; server có thay đổi thì tải lại danh sách
    const unlisten = ipc.listen<{ key?: string }>('cms_updated', (data) => {
      if (data?.key === 'frames') load(true);
    });
    load(false);

    return () => {
      isMounted = false;
      unlisten();
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
                  style={{
                    // Viền ôm sát chính ảnh frame (nút co theo ảnh), không theo ô tỉ lệ của layout
                    boxShadow: isSelected
                      ? '0 0 0 4px #7a0000, 0 0 18px 6px rgba(190, 40, 40, 0.45), 0 0 40px 10px rgba(190, 40, 40, 0.2)'
                      : 'none',
                  }}
                  className={`block p-0 border-0 bg-transparent cursor-pointer transition-all duration-200 ${
                    isSelected ? 'scale-[1.06]' : 'opacity-80 active:scale-[0.98]'
                  }`}
                >
                  <img
                    src={frame.thumbnailUrl || frame.frameUrl}
                    alt=""
                    style={{ aspectRatio: `auto ${aspectRatio}` }}
                    className="block h-[34vh] min-h-[220px] w-auto max-w-full pointer-events-none select-none"
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
