import React from 'react';

interface FavoriLayoutProps {
  children: React.ReactNode;
  stepNumber?: number; // e.g. 1 to 10
  stepTitle?: string;  // e.g. "Chọn kích cỡ ảnh"
  subtitle?: string;
  hideHeader?: boolean;
  onAdminTap?: () => void;
  onBack?: () => void;
  onNext?: () => void;
  nextLabel?: string;
  backLabel?: string;
  disableNext?: boolean;
  centerHeaderStep?: boolean;
  headerStepPosition?: 'center' | 'underLogo';
  /** Số giây còn lại của bước; có giá trị thì hiện "Thời gian còn lại" ở góc trên trái. */
  timeLeft?: number;
  /** Cho phần nội dung dùng gần hết bề ngang màn hình. */
  wide?: boolean;
}

export const FavoriLayout: React.FC<FavoriLayoutProps> = ({
  children,
  stepNumber,
  stepTitle,
  subtitle,
  hideHeader = false,
  onAdminTap,
  onBack,
  onNext,
  nextLabel = 'Tiếp tục',
  backLabel = 'Quay về',
  disableNext = false,
  headerStepPosition = 'center',
  timeLeft,
  wide = false,
}) => {
  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#f5f0ea] flex flex-col justify-between p-8 select-none">
      {/* Universal Background Image with Floral Corner Motifs */}
      <div 
        className="absolute inset-0 pointer-events-none bg-cover bg-center z-0 opacity-90"
        style={{ backgroundImage: `url('/favori/Background.png')` }}
      />

      {/* Màn không có chân trang thì đồng hồ nằm góc trên trái; có chân trang thì nằm giữa hàng nút (bên dưới) */}
      {typeof timeLeft === 'number' && !(onBack || onNext) && (
        <div className="absolute top-3 left-6 z-20 text-[#c1121f] text-base font-semibold tabular-nums">
          Thời gian còn lại: {timeLeft}s
        </div>
      )}

      {/* Top Header Row */}
      {!hideHeader && (
        <header className="relative z-10 w-full grid grid-cols-[1fr_auto_1fr] items-start pt-2 px-6">
          {/* Logo Top Left (with hidden multi-tap admin zone) */}
          <div
            onClick={onAdminTap}
            className="flex flex-col items-start justify-self-start cursor-pointer active:opacity-80 transition-opacity"
          >
            <img 
              src="/favori/logo.png" 
              alt="Favori Logo" 
              className="h-14 object-contain filter drop-shadow-sm" 
            />
            {headerStepPosition === 'underLogo' && stepNumber && (
              <span className="text-[#7a0000] text-sm font-semibold tracking-wider uppercase mt-1">
                BƯỚC {stepNumber}/11
              </span>
            )}
          </div>

          {/* Center Step Header (Standard Favori Title Layout) */}
          {headerStepPosition === 'center' && (stepNumber || stepTitle) ? (
            <div className="flex flex-col items-center justify-center text-center pt-6">
              {stepNumber && (
                <span className="text-[#7a0000] text-lg font-medium tracking-wide mb-1">
                  Bước {stepNumber}/11
                </span>
              )}
              {stepTitle && (
                <h1 className="text-3xl md:text-5xl font-lobster text-[#1f1610] tracking-normal">
                  {stepTitle}
                </h1>
              )}
              {subtitle && (
                <p className="text-sm md:text-base text-[#6b5b52] mt-1 font-normal">
                  {subtitle}
                </p>
              )}
            </div>
          ) : (
            <div />
          )}

          {/* "Photobooth by Favori" đã có sẵn trong Background.png */}
          <div />
        </header>
      )}

      {/* Main Content Body */}
      <main
        className={`relative z-10 flex-1 min-h-0 flex flex-col items-center justify-center w-full mx-auto px-4 py-2 ${
          wide ? 'max-w-[1700px]' : 'max-w-6xl'
        }`}
      >
        {children}
      </main>

      {/* Standard Bottom Navigation Controls */}
      {(onBack || onNext) && (
        <footer className="relative z-10 w-full grid grid-cols-[1fr_auto_1fr] items-center px-8 pb-4">
          <div className="justify-self-start">
            {onBack ? (
              <button
                onClick={onBack}
                className="btn-favori-primary px-8 py-3 text-base md:text-lg min-w-[140px] flex items-center justify-center cursor-pointer shadow-md"
              >
                {backLabel}
              </button>
            ) : <div />}
          </div>

          {typeof timeLeft === 'number' ? (
            <div className="flex flex-col items-center leading-tight text-[#c1121f]">
              <span className="text-xs font-semibold">Thời gian còn lại</span>
              <span className="text-3xl font-bold tabular-nums">{timeLeft}s</span>
            </div>
          ) : (
            <div />
          )}

          <div className="justify-self-end">
            {onNext && (
              <button
                onClick={onNext}
                disabled={disableNext}
                className={`btn-favori-primary px-10 py-3 text-base md:text-lg min-w-[160px] flex items-center justify-center cursor-pointer shadow-lg ${
                  disableNext ? 'opacity-40 cursor-not-allowed' : 'hover:brightness-105 active:scale-95'
                }`}
              >
                {nextLabel}
              </button>
            )}
          </div>
        </footer>
      )}
    </div>
  );
};
