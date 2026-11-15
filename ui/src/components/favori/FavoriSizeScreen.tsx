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
                <div
                  className={`rounded-[20px] overflow-hidden transition-all duration-200 ${
                    isSelected ? 'ring-4 ring-[#7a0000] scale-[1.03] shadow-xl' : 'hover:scale-[1.01]'
                  }`}
                >
                  {image ? (
                    <img
                      src={image}
                      alt=""
                      className="w-[340px] md:w-[368px] block select-none pointer-events-none"
                    />
                  ) : (
                    <div className="w-[340px] md:w-[368px] h-[420px] bg-white flex items-center justify-center">
                      <div
                        className="h-[320px] border-2 border-dashed border-[#7a0000]/60 bg-[#f5f0ea]"
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
