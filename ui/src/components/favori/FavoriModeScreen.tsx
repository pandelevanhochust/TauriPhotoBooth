import React from 'react';
import { FavoriLayout } from './FavoriLayout';

export type CaptureMode = 'remote' | 'autoCountdown';

interface FavoriModeScreenProps {
  selectedMode: CaptureMode;
  /** Tổng thời gian của chế độ nút bấm (giây). */
  remoteTimeoutSec?: number;
  /** Số giây đếm ngược mỗi tấm ở chế độ tự động. */
  countdownSec?: number;
  onSelectMode: (mode: CaptureMode) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriModeScreen: React.FC<FavoriModeScreenProps> = ({
  selectedMode,
  remoteTimeoutSec = 180,
  countdownSec = 10,
  onSelectMode,
  onNext,
  onBack,
}) => {
  return (
    <FavoriLayout
      stepNumber={5}
      stepTitle="Chọn chế độ chụp"
      onBack={onBack}
      onNext={undefined}
    >
      <div className="grid grid-cols-1 md:grid-cols-2 gap-10 w-full max-w-4xl justify-center items-stretch py-6">
        {/* Option 1: Remote Control */}
        <div
          onClick={() => {
            onSelectMode('remote');
            onNext();
          }}
          className={`favori-card flex items-center p-8 cursor-pointer gap-6 transition-all ${
            selectedMode === 'remote'
              ? 'ring-3 ring-[#7a0000] scale-102 shadow-xl'
              : 'hover:scale-[1.01] hover:border-[#7a0000]/60'
          }`}
        >
          <div className="w-32 h-32 flex-shrink-0 flex items-center justify-center">
            <img
              src="/favori/page6_option1.png"
              alt="Remote Button"
              className="max-h-full max-w-full object-contain filter drop-shadow-sm"
            />
          </div>
          <div className="flex-1 text-left">
            <h3 className="text-2xl md:text-3xl font-lobster text-[#1f1610] leading-snug">
              Chụp bằng nút bấm
            </h3>
            <p className="text-base text-[#5c4d43] mt-1 font-medium">
              trong {Math.max(1, Math.round(remoteTimeoutSec / 60))} phút
            </p>
          </div>
        </div>

        {/* Option 2: 10s Countdown */}
        <div
          onClick={() => {
            onSelectMode('autoCountdown');
            onNext();
          }}
          className={`favori-card flex items-center p-8 cursor-pointer gap-6 transition-all ${
            selectedMode === 'autoCountdown'
              ? 'ring-3 ring-[#7a0000] scale-102 shadow-xl'
              : 'hover:scale-[1.01] hover:border-[#7a0000]/60'
          }`}
        >
          <div className="w-32 h-32 flex-shrink-0 flex items-center justify-center">
            <img
              src="/favori/page6_option2.png"
              alt="Clock Countdown"
              className="max-h-full max-w-full object-contain filter drop-shadow-sm"
            />
          </div>
          <div className="flex-1 text-left">
            <h3 className="text-2xl md:text-3xl font-lobster text-[#1f1610] leading-snug">
              {countdownSec} giây đếm ngược
            </h3>
            <p className="text-base text-[#5c4d43] mt-1 font-medium">
              tự động
            </p>
          </div>
        </div>
      </div>

      <p className="text-sm text-[#6b5b52] text-center">
        Ảnh được lưu 24 giờ để bạn tải về, sau đó tự động xoá.
      </p>
    </FavoriLayout>
  );
};
