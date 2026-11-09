import React, { useState } from 'react';

interface FavoriWelcomeScreenProps {
  onStart: () => void;
  onOpenAdmin: () => void;
  language: 'vi' | 'en';
  onLanguageChange: (lang: 'vi' | 'en') => void;
  paperCount: number;
}

export const FavoriWelcomeScreen: React.FC<FavoriWelcomeScreenProps> = ({
  onStart,
  onOpenAdmin,
  language,
  onLanguageChange,
  paperCount,
}) => {
  const [tapCount, setTapCount] = useState<number>(0);
  const [lastTapTime, setLastTapTime] = useState<number>(0);

  const handleAdminTap = () => {
    const now = Date.now();
    if (now - lastTapTime < 2500) {
      const newCount = tapCount + 1;
      if (newCount >= 3) {
        setTapCount(0);
        onOpenAdmin();
      } else {
        setTapCount(newCount);
      }
    } else {
      setTapCount(1);
    }
    setLastTapTime(now);
  };

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#f5f0ea] flex flex-col justify-between p-8 select-none">
      {/* Background with Corner Filigree */}
      <div 
        className="absolute inset-0 pointer-events-none bg-cover bg-center z-0 opacity-95"
        style={{ backgroundImage: `url('/favori/Background.png')` }}
      />

      {/* Top Bar with Top-Left Secret Tap Area and Top-Right Branding */}
      <header className="relative z-10 w-full flex items-center justify-between pt-2 px-6">
        <div 
          onClick={handleAdminTap} 
          className="w-28 h-20 cursor-pointer opacity-0 hover:opacity-10 transition-opacity" 
        />
        <div className="w-28" />
      </header>

      {/* Center Hero Section Matching 1.png */}
      <main className="relative z-10 flex flex-col items-center justify-center my-auto text-center space-y-6">
        {/* Favori Calligraphic Logo */}
        <div 
          onClick={handleAdminTap}
          className="w-full max-w-md flex justify-center animate-soft-pulse cursor-pointer select-none active:scale-[0.98] transition-transform"
        >
          <img 
            src="/favori/logo.png" 
            alt="Favori" 
            className="w-72 md:w-96 object-contain filter drop-shadow-md" 
          />
        </div>

        {/* Vintage Italic Tagline in Lobster font */}
        <p className="font-lobster text-2xl md:text-3xl text-[#3b2d24] tracking-wide">
          {language === 'vi' ? 'Lưu lại khoảng khắc đẹp nhất của bạn' : 'Capture your most beautiful moments'}
        </p>

        {/* Language Selection Buttons (Pill buttons side-by-side) */}
        <div className="flex items-center justify-center gap-6 pt-4">
          <button
            onClick={() => onLanguageChange('vi')}
            className={`px-8 py-3 rounded-full text-lg md:text-xl font-medium min-w-[150px] transition-all cursor-pointer shadow-md ${
              language === 'vi'
                ? 'bg-[#7a0000] text-white shadow-[#7a0000]/30 scale-105'
                : 'bg-white/80 text-[#4a3b32] border border-[#c5a880]/50 hover:bg-white'
            }`}
          >
            Tiếng Việt
          </button>

          <button
            onClick={() => onLanguageChange('en')}
            className={`px-8 py-3 rounded-full text-lg md:text-xl font-medium min-w-[150px] transition-all cursor-pointer shadow-md ${
              language === 'en'
                ? 'bg-[#7a0000] text-white shadow-[#7a0000]/30 scale-105'
                : 'bg-white/80 text-[#4a3b32] border border-[#c5a880]/50 hover:bg-white'
            }`}
          >
            English
          </button>
        </div>
      </main>

      {/* Bottom Center "Tiếp tục" Action Button */}
      <footer className="relative z-10 w-full flex flex-col items-center justify-center pb-6">
        <button
          onClick={onStart}
          disabled={paperCount <= 0}
          className="btn-favori-primary px-14 py-3.5 text-xl font-medium min-w-[200px] cursor-pointer hover:brightness-110 active:scale-95 shadow-xl"
        >
          {language === 'vi' ? 'Tiếp tục' : 'Continue'}
        </button>

        {paperCount <= 0 && (
          <span className="mt-3 text-red-600 font-medium text-sm">
            Kiosk hết giấy in. Vui lòng liên hệ nhân viên!
          </span>
        )}
      </footer>
    </div>
  );
};
