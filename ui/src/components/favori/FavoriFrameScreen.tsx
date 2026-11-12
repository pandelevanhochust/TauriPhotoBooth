import React, { useState, useEffect, useMemo } from 'react';
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

type LayoutTab = 'all' | '1x4' | '2x2' | '2x3';

export const FavoriFrameScreen: React.FC<FavoriFrameScreenProps> = ({
  selectedSize,
  selectedFrame,
  onSelectFrame,
  onNext,
  onBack,
}) => {
  const [allFrames, setAllFrames] = useState<FrameOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  // Xác định tab mặc định dựa trên selectedSize từ Bước 1
  const initialTab = useMemo<LayoutTab>(() => {
    const s = (selectedSize || '').toLowerCase();
    if (s.includes('2x6') || s === 'strip2x6' || s === '1x4') return '1x4';
    if (s === 'postcard4x6' || s === '2x2') return '2x2';
    if (s === 'special4x6' || s === '2x3') return '2x3';
    return 'all';
  }, [selectedSize]);

  const [activeTab, setActiveTab] = useState<LayoutTab>(initialTab);

  useEffect(() => {
    setActiveTab(initialTab);
  }, [initialTab]);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);

    backendApi.getFrames().then((data) => {
      if (!isMounted) return;
      console.log('[FavoriFrameScreen] Loaded all frames from backend:', data);
      setAllFrames(data);
      setLoading(false);

      if (data.length > 0) {
        // Nếu đã có selectedFrame hợp lệ trong data thì giữ nguyên
        const matched = data.find((f) => f.id === selectedFrame);
        if (matched) {
          onSelectFrame(matched.id, matched);
        } else {
          // Ưu tiên chọn frame đầu tiên của layout hiện tại nếu có
          const layoutMatched = data.find((f) => f.layoutId === initialTab);
          const firstFrame = layoutMatched || data[0];
          onSelectFrame(firstFrame.id, firstFrame);
        }
      }
    });

    return () => {
      isMounted = false;
    };
  }, []);

  // Lọc frames theo tab đang chọn
  const displayedFrames = useMemo(() => {
    if (activeTab === 'all') return allFrames;
    return allFrames.filter((f) => f.layoutId === activeTab);
  }, [allFrames, activeTab]);

  const tabs: { id: LayoutTab; label: string; count: number }[] = [
    { id: 'all', label: 'Tất cả khung', count: allFrames.length },
    { id: '1x4', label: 'Dải 4 ảnh (1x4)', count: allFrames.filter((f) => f.layoutId === '1x4').length },
    { id: '2x2', label: 'Lưới 4 ảnh vuông (2x2)', count: allFrames.filter((f) => f.layoutId === '2x2').length },
    { id: '2x3', label: 'Lưới 6 ảnh (2x3)', count: allFrames.filter((f) => f.layoutId === '2x3').length },
  ];

  return (
    <FavoriLayout
      stepNumber={2}
      stepTitle="Chọn khung ảnh"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="flex flex-col items-center w-full max-w-6xl py-1 px-4">
        {/* Header Tabs phân loại bố cục */}
        <div className="flex flex-wrap items-center justify-center gap-2 md:gap-3 mb-4 bg-white/60 p-1.5 rounded-full shadow-sm backdrop-blur-sm border border-[#e8dcce]">
          {tabs.map((tab) => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveTab(tab.id)}
                className={`px-4 py-1.5 rounded-full text-sm font-medium transition-all duration-200 flex items-center space-x-1.5 ${
                  isActive
                    ? 'bg-[#7a0000] text-white shadow-md scale-102 font-semibold'
                    : 'text-[#6b5b52] hover:bg-[#7a0000]/10 hover:text-[#7a0000]'
                }`}
              >
                <span>{tab.label}</span>
                {tab.count > 0 && (
                  <span
                    className={`text-xs px-1.5 py-0.2 rounded-full ${
                      isActive ? 'bg-white/25 text-white' : 'bg-neutral-200 text-neutral-600'
                    }`}
                  >
                    {tab.count}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {/* Danh sách khung ảnh */}
        {loading ? (
          <div className="flex flex-col items-center justify-center py-24 space-y-3">
            <Loader2 className="w-10 h-10 animate-spin text-[#7a0000]" />
            <span className="text-[#6b5b52] font-medium text-base">
              Đang tải danh sách khung từ máy chủ CMS...
            </span>
          </div>
        ) : displayedFrames.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 space-y-3">
            <ImageIcon className="w-12 h-12 text-neutral-400" />
            <span className="text-[#6b5b52] font-medium text-base">
              Không tìm thấy khung ảnh trong danh mục này.
            </span>
            <button
              onClick={() => setActiveTab('all')}
              className="mt-2 text-sm text-[#7a0000] underline font-semibold cursor-pointer"
            >
              Xem tất cả các khung ảnh
            </button>
          </div>
        ) : (
          <div className="flex flex-wrap items-center justify-center gap-6 md:gap-10 w-full py-6">
            {displayedFrames.map((frame) => {
              const isSelected = selectedFrame === frame.id;
              const isStrip = frame.layoutId === '1x4';

              return (
                <button
                  key={frame.id}
                  type="button"
                  onClick={() => onSelectFrame(frame.id, frame)}
                  title={frame.name}
                  className={`group relative flex items-center justify-center p-0 border-0 bg-transparent cursor-pointer transition-all duration-300 outline-none focus:outline-none ${
                    isStrip
                      ? 'w-28 md:w-36 h-80 md:h-96'
                      : 'w-52 md:w-64 h-78 md:h-96'
                  } ${
                    isSelected
                      ? 'scale-105 z-10'
                      : 'hover:scale-103 opacity-90 hover:opacity-100'
                  }`}
                >
                  {/* Khung ảnh thuần túy */}
                  <div
                    className={`w-full h-full rounded-2xl overflow-hidden transition-all duration-300 flex items-center justify-center ${
                      isSelected
                        ? 'ring-4 ring-[#7a0000] ring-offset-4 ring-offset-[#fcfbf8] drop-shadow-[0_16px_32px_rgba(122,0,0,0.35)]'
                        : 'drop-shadow-md group-hover:drop-shadow-2xl'
                    }`}
                  >
                    {frame.thumbnailUrl || frame.frameUrl ? (
                      <img
                        src={frame.thumbnailUrl || frame.frameUrl}
                        alt={frame.name}
                        className="w-full h-full object-contain pointer-events-none select-none transition-transform duration-300 group-hover:scale-[1.01]"
                        loading="lazy"
                        onError={(e) => {
                          if (frame.frameUrl && (e.currentTarget as HTMLImageElement).src !== frame.frameUrl) {
                            (e.currentTarget as HTMLImageElement).src = frame.frameUrl;
                          }
                        }}
                      />
                    ) : (
                      /* Fallback nếu chưa tải được ảnh */
                      <div className="w-full h-full flex flex-col items-center justify-center bg-white/70 border border-neutral-200 rounded-2xl p-4">
                        <ImageIcon className="w-8 h-8 text-neutral-400 mb-2" />
                        <span className="text-xs text-neutral-500 font-medium text-center">
                          {frame.name}
                        </span>
                      </div>
                    )}
                  </div>
                </button>
              );
            })}
          </div>
        )}
      </div>
    </FavoriLayout>
  );
};
