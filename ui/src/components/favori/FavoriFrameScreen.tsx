import React, { useState, useEffect, useMemo } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2, Image as ImageIcon, Sparkles } from 'lucide-react';
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
          <div className="flex flex-wrap items-stretch justify-center gap-6 w-full py-2">
            {displayedFrames.map((frame) => {
              const isSelected = selectedFrame === frame.id;
              return (
                <div
                  key={frame.id}
                  onClick={() => onSelectFrame(frame.id, frame)}
                  className={`favori-card flex flex-col items-center p-3 cursor-pointer w-48 md:w-56 transition-all duration-200 relative ${
                    isSelected
                      ? 'ring-4 ring-[#7a0000] scale-105 shadow-2xl bg-white'
                      : 'hover:scale-102 hover:border-[#7a0000]/50 bg-white/80'
                  }`}
                >
                  {/* Category badge */}
                  {frame.categoryName && (
                    <div className="absolute top-2 left-2 z-10 bg-[#7a0000]/90 text-white text-[10px] px-2 py-0.5 rounded-md font-medium tracking-wide shadow-sm flex items-center space-x-1">
                      <Sparkles className="w-2.5 h-2.5" />
                      <span>{frame.categoryName}</span>
                    </div>
                  )}

                  {/* Frame Thumbnail / Preview Image */}
                  <div
                    className="w-full h-56 rounded-xl flex items-center justify-center p-2 border border-neutral-300 shadow-inner mb-2 overflow-hidden relative bg-neutral-100"
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
                    <div className="absolute top-2 right-2 bg-black/70 text-white text-[10px] px-2 py-0.5 rounded-full uppercase tracking-wider backdrop-blur-sm font-semibold">
                      {frame.layoutId}
                    </div>
                  </div>

                  {/* Title */}
                  <span className="font-lobster text-lg text-[#1f1610] text-center leading-snug line-clamp-2 min-h-[46px] flex items-center justify-center px-1">
                    {frame.name}
                  </span>

                  {/* Radio indicator */}
                  <div className="mt-2 flex items-center justify-center">
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
