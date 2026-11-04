import React from 'react';
import { FavoriLayout } from './FavoriLayout';

export type PhotoSizeType = 'strip2x6' | 'postcard4x6' | 'special4x6';

interface FavoriSizeScreenProps {
  selectedSize: PhotoSizeType;
  onSelectSize: (size: PhotoSizeType) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriSizeScreen: React.FC<FavoriSizeScreenProps> = ({
  selectedSize,
  onSelectSize,
  onNext,
  onBack,
}) => {
  const options = [
    {
      id: 'strip2x6' as PhotoSizeType,
      image: '/favori/page2_option1.png',
      title: '5 x 15 cm - Dọc',
      description: 'In theo cặp chẵn: 2, 4, 6...',
    },
    {
      id: 'postcard4x6' as PhotoSizeType,
      image: '/favori/page2_option2.png',
      title: '10 x 15 cm - To',
      description: 'In số lượng tùy ý, ít nhất từ 2 ảnh',
    },
    {
      id: 'special4x6' as PhotoSizeType,
      image: '/favori/page2_option3.png',
      title: 'ảnh 10 × 15 - Đặc biệt',
      description: '1 ảnh 10×15 kèm khung và móc khoá tự chọn',
    },
  ];

  return (
    <FavoriLayout
      stepNumber={1}
      stepTitle="Chọn kích cỡ ảnh"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="grid grid-cols-1 md:grid-cols-3 gap-8 w-full max-w-5xl justify-center items-stretch py-4">
        {options.map((opt) => {
          const isSelected = selectedSize === opt.id;
          return (
            <div
              key={opt.id}
              onClick={() => onSelectSize(opt.id)}
              className={`favori-card flex flex-col items-center justify-between p-6 cursor-pointer text-center transition-all ${
                isSelected
                  ? 'ring-3 ring-[#7a0000] scale-[1.02] shadow-xl'
                  : 'hover:scale-[1.01] hover:border-[#7a0000]/60'
              }`}
            >
              {/* Graphic Icon */}
              <div className="h-44 w-full flex items-center justify-center p-2 mb-4">
                <img
                  src={opt.image}
                  alt={opt.title}
                  className="max-h-full max-w-full object-contain filter drop-shadow-sm"
                />
              </div>

              {/* Text Info */}
              <div className="flex flex-col items-center space-y-2 mt-auto">
                <h3 className="text-xl md:text-2xl font-serif-vintage italic font-bold text-[#1f1610]">
                  {opt.title}
                </h3>
                <p className="text-sm md:text-base text-[#5c4d43] leading-relaxed">
                  {opt.description}
                </p>
              </div>

              {/* Check indicator dot */}
              <div className="mt-4">
                <div
                  className={`w-6 h-6 rounded-full border-2 flex items-center justify-center transition-all ${
                    isSelected
                      ? 'border-[#7a0000] bg-[#7a0000]'
                      : 'border-[#c5a880] bg-transparent'
                  }`}
                >
                  {isSelected && <div className="w-2.5 h-2.5 rounded-full bg-white" />}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </FavoriLayout>
  );
};
