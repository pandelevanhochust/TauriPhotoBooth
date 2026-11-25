import React, { useEffect, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { FavoriLayout } from './FavoriLayout';
import { backendApi, type LayoutOption } from '../../services/backendApi';

// Ảnh minh hoạ có sẵn cho từng layout của CMS; layout lạ sẽ được vẽ bằng thẻ chữ.
const LAYOUT_IMAGES: Record<string, string> = {
  '1x4': '/favori/page2_option1.png',
  '2x2': '/favori/page2_option2.png',
};

interface FavoriSizeScreenProps {
  selectedLayoutId?: string;
  onSelectLayout: (layout: LayoutOption) => void;
  showPrice?: boolean;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriSizeScreen: React.FC<FavoriSizeScreenProps> = ({
  selectedLayoutId,
  onSelectLayout,
  showPrice = false,
  onNext,
  onBack,
}) => {
  const [layouts, setLayouts] = useState<LayoutOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  useEffect(() => {
    let isMounted = true;
    backendApi.getLayouts().then((data) => {
      if (!isMounted) return;
      setLayouts(data);
      setLoading(false);
    });
    return () => {
      isMounted = false;
    };
  }, []);

  const hasSelection = layouts.some((l) => l.id === selectedLayoutId);

  return (
    <FavoriLayout
      stepNumber={1}
      stepTitle="Chọn kích cỡ ảnh"
      onBack={onBack}
      onNext={onNext}
      disableNext={!hasSelection}
    >
      {loading ? (
        <div className="flex flex-col items-center justify-center py-16 space-y-3">
          <Loader2 className="w-10 h-10 animate-spin text-[#7a0000]" />
          <span className="text-[#6b5b52] font-medium">Đang tải kích cỡ ảnh...</span>
        </div>
      ) : (
        <div className="flex flex-wrap items-start justify-center gap-10 md:gap-16 w-full py-6">
          {layouts.map((layout) => {
            const isSelected = selectedLayoutId === layout.id;
            const image = LAYOUT_IMAGES[layout.id];
            const tier = layout.pricingTiers[0];

            return (
              <button
                key={layout.id}
                type="button"
                onClick={() => onSelectLayout(layout)}
                aria-pressed={isSelected}
                aria-label={layout.name}
                className="flex flex-col items-center gap-3 p-0 border-0 bg-transparent cursor-pointer focus:outline-none active:scale-[0.98] transition-transform duration-200"
              >
                {/* Ảnh minh hoạ không còn viền sẵn: viền be như thiết kế, chọn thì viền đỏ phát sáng giống frame ở bước 2 */}
                <div
                  style={{
                    boxShadow: isSelected
                      ? '0 0 18px 6px rgba(190, 40, 40, 0.45), 0 0 40px 10px rgba(190, 40, 40, 0.2)'
                      : 'none',
                  }}
                  className={`rounded-[1.75rem] overflow-hidden bg-white border-4 transition-all duration-200 ${
                    isSelected ? 'border-[#7a0000] scale-[1.03]' : 'border-[#EDE1C8]'
                  }`}
                >
                  {image ? (
                    <img
                      src={image}
                      alt=""
                      className="w-[21.25rem] md:w-[23rem] block select-none pointer-events-none"
                    />
                  ) : (
                    <div className="w-[21.25rem] md:w-[23rem] h-[26.25rem] bg-white flex items-center justify-center">
                      <div
                        className="h-[20rem] border-2 border-dashed border-[#7a0000]/60 bg-[#f5f0ea]"
                        style={{ aspectRatio: `${layout.widthPx} / ${layout.heightPx}` }}
                      />
                    </div>
                  )}
                </div>

                <span className="font-lobster text-2xl text-[#1f1610]">{layout.name}</span>
                {showPrice && tier && (
                  <span className="text-[#7a0000] text-base font-semibold -mt-2">
                    {tier.price.toLocaleString('vi-VN')} đ / {tier.quantity} bản
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </FavoriLayout>
  );
};
