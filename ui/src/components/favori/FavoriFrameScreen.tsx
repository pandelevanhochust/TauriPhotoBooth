import React from 'react';
import { FavoriLayout } from './FavoriLayout';

export interface FrameTheme {
  id: string;
  name: string;
  bgColor: string;
  borderColor: string;
  previewUrl?: string;
}

interface FavoriFrameScreenProps {
  selectedFrame: string;
  onSelectFrame: (id: string) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriFrameScreen: React.FC<FavoriFrameScreenProps> = ({
  selectedFrame,
  onSelectFrame,
  onNext,
  onBack,
}) => {
  const frames: FrameTheme[] = [
    {
      id: 'classic-cream',
      name: 'Kem Cổ Điển',
      bgColor: '#fdfbf7',
      borderColor: '#e8dcce',
    },
    {
      id: 'pure-white',
      name: 'Trắng Tinh Khôi',
      bgColor: '#ffffff',
      borderColor: '#e5e5e5',
    },
    {
      id: 'vintage-crimson',
      name: 'Đỏ Burgundy',
      bgColor: '#7a0000',
      borderColor: '#9e1c1c',
    },
    {
      id: 'midnight-black',
      name: 'Đen Sang Trọng',
      bgColor: '#1a1918',
      borderColor: '#3a3836',
    },
    {
      id: 'floral-ornament',
      name: 'Họa Tiết Cổ Điển',
      bgColor: '#f5f0ea',
      borderColor: '#c5a880',
    },
  ];

  return (
    <FavoriLayout
      stepNumber={2}
      stepTitle="Chọn khung"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="flex flex-wrap items-center justify-center gap-6 w-full max-w-5xl py-4">
        {frames.map((frame) => {
          const isSelected = selectedFrame === frame.id;
          return (
            <div
              key={frame.id}
              onClick={() => onSelectFrame(frame.id)}
              className={`favori-card flex flex-col items-center p-5 cursor-pointer w-48 md:w-56 transition-all ${
                isSelected
                  ? 'ring-3 ring-[#7a0000] scale-105 shadow-xl'
                  : 'hover:scale-102 hover:border-[#7a0000]/50'
              }`}
            >
              {/* Frame Mockup Mini Preview */}
              <div 
                className="w-full h-44 rounded-lg flex flex-col items-center justify-around p-3 border shadow-inner mb-4"
                style={{ 
                  backgroundColor: frame.bgColor,
                  borderColor: frame.borderColor 
                }}
              >
                {/* 3 mini photo mock apertures */}
                <div className="w-4/5 h-9 bg-neutral-200/80 rounded-sm border border-neutral-300" />
                <div className="w-4/5 h-9 bg-neutral-200/80 rounded-sm border border-neutral-300" />
                <div className="w-4/5 h-9 bg-neutral-200/80 rounded-sm border border-neutral-300" />
                <span 
                  className={`text-[10px] font-serif italic ${
                    frame.bgColor === '#7a0000' || frame.bgColor === '#1a1918'
                      ? 'text-white/80'
                      : 'text-neutral-600'
                  }`}
                >
                  Favori
                </span>
              </div>

              {/* Title & selection */}
              <span className="font-serif-vintage italic font-bold text-lg text-[#1f1610] text-center">
                {frame.name}
              </span>

              <div className="mt-3">
                <div
                  className={`w-5 h-5 rounded-full border-2 flex items-center justify-center ${
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
    </FavoriLayout>
  );
};
