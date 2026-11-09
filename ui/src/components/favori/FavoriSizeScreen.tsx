import React from 'react';
import { FavoriLayout } from './FavoriLayout';

export type PhotoSizeType = 'strip2x6' | 'postcard4x6' | 'special4x6';

interface SizeOption {
  id: PhotoSizeType;
  image: string;
  title: string;
}

const SIZE_OPTIONS: SizeOption[] = [
  {
    id: 'strip2x6',
    image: '/favori/page2_option1.png',
    title: '5 x 15 cm - Dọc',
  },
  {
    id: 'postcard4x6',
    image: '/favori/page2_option2.png',
    title: '10 x 15 cm - To',
  },
  {
    id: 'special4x6',
    image: '/favori/page2_option3.png',
    title: 'ảnh 10 × 15 - Đặc biệt',
  },
];

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
  return (
    <FavoriLayout
      stepNumber={1}
      stepTitle="Chọn kích cỡ ảnh"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 md:gap-8 w-full max-w-5xl justify-center items-center py-6">
        {SIZE_OPTIONS.map((opt) => {
          const isSelected = selectedSize === opt.id;
          return (
            <button
              key={opt.id}
              type="button"
              onClick={() => onSelectSize(opt.id)}
              aria-pressed={isSelected}
              aria-label={opt.title}
              className={`p-0 m-0 border-0 bg-transparent cursor-pointer transition-all duration-200 focus:outline-none focus-visible:ring-4 focus-visible:ring-[#7a0000] rounded-[20px] overflow-hidden ${
                isSelected
                  ? 'ring-4 ring-[#7a0000] scale-[1.03] shadow-xl'
                  : 'hover:scale-[1.01]'
              } active:scale-[0.98]`}
            >
              <img
                src={opt.image}
                alt=""
                className="w-full max-w-[340px] md:max-w-[368px] block select-none pointer-events-none rounded-[20px]"
              />
            </button>
          );
        })}
      </div>
    </FavoriLayout>
  );
};
