import React, { useState } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { RotateCcw, Check } from 'lucide-react';

interface FavoriPhotoSelectScreenProps {
  photos: string[];
  maxRetakes?: number;
  onRetake: (index: number) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriPhotoSelectScreen: React.FC<FavoriPhotoSelectScreenProps> = ({
  photos,
  maxRetakes = 1,
  onRetake,
  onNext,
  onBack,
}) => {
  const [retakesUsed, setRetakesUsed] = useState<number>(0);
  const [selectedPhotoIndex, setSelectedPhotoIndex] = useState<number | null>(null);

  const handleRetakeClick = () => {
    if (selectedPhotoIndex !== null && retakesUsed < maxRetakes) {
      setRetakesUsed((r) => r + 1);
      onRetake(selectedPhotoIndex);
    }
  };

  return (
    <FavoriLayout
      stepNumber={9}
      stepTitle="Chọn ảnh để in"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="flex flex-col items-center w-full max-w-5xl py-2 space-y-6">
        {/* Photos Grid */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-6 w-full">
          {photos.map((photo, index) => {
            const isSelected = selectedPhotoIndex === index;
            return (
              <div
                key={index}
                onClick={() => setSelectedPhotoIndex(index)}
                className={`favori-card relative overflow-hidden flex flex-col p-3 cursor-pointer transition-all ${
                  isSelected
                    ? 'ring-3 ring-[#7a0000] scale-102 shadow-2xl'
                    : 'hover:scale-101 hover:border-[#7a0000]/50'
                }`}
              >
                {/* Photo image */}
                <div className="w-full aspect-[3/4] rounded-lg overflow-hidden bg-neutral-900 border border-neutral-300">
                  <img
                    src={photo}
                    alt={`Shot ${index + 1}`}
                    className="w-full h-full object-cover"
                  />
                </div>

                {/* Card footer */}
                <div className="flex items-center justify-between mt-3 px-1">
                  <span className="font-serif-vintage italic font-bold text-base text-[#1f1610]">
                    Ảnh {index + 1}
                  </span>
                  <div className="w-5 h-5 rounded-full bg-[#7a0000] text-white flex items-center justify-center text-xs">
                    <Check className="w-3.5 h-3.5" />
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Retake Control Bar */}
        <div className="flex items-center gap-4 pt-2">
          <button
            onClick={handleRetakeClick}
            disabled={selectedPhotoIndex === null || retakesUsed >= maxRetakes}
            className={`px-6 py-2.5 rounded-full text-base font-medium flex items-center gap-2 border transition-all ${
              selectedPhotoIndex !== null && retakesUsed < maxRetakes
                ? 'bg-white text-[#7a0000] border-[#7a0000] hover:bg-[#7a0000]/10 cursor-pointer shadow-md'
                : 'bg-neutral-100 text-neutral-400 border-neutral-200 cursor-not-allowed opacity-60'
            }`}
          >
            <RotateCcw className="w-4 h-4" />
            {retakesUsed >= maxRetakes
              ? 'Đã hết lượt chụp lại (Tối đa 1 lần)'
              : selectedPhotoIndex !== null
              ? `Chụp lại Ảnh ${selectedPhotoIndex + 1}`
              : 'Chọn 1 ảnh để chụp lại'}
          </button>
        </div>
      </div>
    </FavoriLayout>
  );
};
