import React, { useState, useRef } from 'react';
import { Camera, Sparkles, Printer, Shield } from 'lucide-react';

interface StandbyScreenProps {
  onStartSession: () => void;
  onOpenAdmin: () => void;
  paperCount: number;
  kioskMode: string;
}

export const StandbyScreen: React.FC<StandbyScreenProps> = ({
  onStartSession,
  onOpenAdmin,
  paperCount,
  kioskMode,
}) => {
  const [tapCount, setTapCount] = useState(0);
  const tapTimerRef = useRef<any>(null);

  const handleSecretTap = (e: React.MouseEvent) => {
    e.stopPropagation();
    const newCount = tapCount + 1;
    setTapCount(newCount);

    if (tapTimerRef.current) clearTimeout(tapTimerRef.current);

    if (newCount >= 5) {
      setTapCount(0);
      onOpenAdmin();
    } else {
      tapTimerRef.current = setTimeout(() => {
        setTapCount(0);
      }, 2000);
    }
  };

  return (
    <div 
      onClick={onStartSession}
      className="relative w-screen h-screen bg-gradient-to-br from-[#0c0d14] via-[#1a172e] to-[#0c0d14] flex flex-col items-center justify-between p-12 cursor-pointer select-none overflow-hidden"
    >
      {/* Hidden 5-tap Admin trigger in top-left corner */}
      <div 
        onClick={handleSecretTap}
        className="absolute top-0 left-0 w-24 h-24 z-50 flex items-start justify-start p-4 opacity-10 hover:opacity-100 transition-opacity"
        title="Admin tap zone"
      >
        <Shield className="w-5 h-5 text-gray-500" />
      </div>

      {/* Top Status Bar */}
      <div className="w-full flex justify-between items-center z-10 text-xs tracking-wider uppercase text-gray-400">
        <div className="flex items-center gap-2 bg-black/40 px-4 py-2 rounded-full border border-white/10 backdrop-blur-md">
          <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse"></span>
          <span>{kioskMode === 'FreeEvent' ? 'Sự Kiện Tự Do (Free)' : 'Chế Độ Bán Vé'}</span>
        </div>

        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 bg-black/40 px-4 py-2 rounded-full border border-white/10 backdrop-blur-md">
            <Printer className="w-4 h-4 text-rose-400" />
            <span>Giấy còn: <strong className="text-white">{paperCount}</strong> bản</span>
          </div>
        </div>
      </div>

      {/* Main Center Attract Elements */}
      <div className="flex flex-col items-center justify-center my-auto z-10 text-center max-w-2xl">
        {/* Glow badge */}
        <div className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full bg-gradient-to-r from-rose-500/20 to-purple-500/20 border border-rose-500/30 text-rose-300 mb-8 backdrop-blur-md">
          <Sparkles className="w-5 h-5 text-rose-400 animate-spin" style={{ animationDuration: '6s' }} />
          <span className="font-semibold text-sm tracking-wide">TRẢI NGHIỆM CHỤP ẢNH TỨC THÌ</span>
        </div>

        {/* Brand Logo & Title */}
        <h1 className="text-6xl md:text-8xl font-black tracking-tight text-transparent bg-clip-text bg-gradient-to-r from-rose-400 via-fuchsia-300 to-indigo-400 mb-6 drop-shadow-2xl">
          HIHI BOOTH
        </h1>

        <p className="text-lg md:text-xl text-gray-300 font-light mb-12 tracking-wide max-w-lg">
          Lưu giữ những khoảnh khắc rạng rỡ cùng bạn bè với khung hình độc quyền và video recap cực nét.
        </p>

        {/* Big Pulsing Start Touch Button */}
        <div className="group relative">
          <div className="absolute -inset-1 bg-gradient-to-r from-rose-600 via-purple-600 to-indigo-600 rounded-3xl blur-xl opacity-75 group-hover:opacity-100 transition duration-1000 group-hover:duration-200 animate-pulse-glow"></div>
          
          <button className="relative px-12 py-7 rounded-3xl bg-gradient-to-r from-rose-500 to-purple-600 text-white font-bold text-2xl md:text-3xl tracking-wider shadow-2xl flex items-center gap-4 transform active:scale-95 transition-transform duration-150">
            <Camera className="w-9 h-9" />
            <span>CHẠM ĐỂ BẮT ĐẦU</span>
          </button>
        </div>
      </div>

      {/* Bottom Footer Info */}
      <div className="w-full flex justify-center items-center z-10 text-gray-500 text-xs tracking-wider">
        <span>HIHIBOOTH v1.0 • Chạm bất kỳ đâu trên màn hình để trải nghiệm</span>
      </div>

      {/* Decorative background ambient glows */}
      <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-rose-600/15 rounded-full blur-3xl pointer-events-none"></div>
      <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-purple-600/15 rounded-full blur-3xl pointer-events-none"></div>
    </div>
  );
};
