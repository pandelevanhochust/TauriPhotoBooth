import React, { useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';

interface FavoriCountdownScreenProps {
  onReady: () => void;
}

export const FavoriCountdownScreen: React.FC<FavoriCountdownScreenProps> = ({ onReady }) => {
  useEffect(() => {
    const timer = setTimeout(() => {
      onReady();
    }, 2800);
    return () => clearTimeout(timer);
  }, [onReady]);

  return (
    <FavoriLayout
      stepNumber={6}
      stepTitle="Chuẩn bị chụp"
      hideHeader={false}
    >
      <div className="flex flex-col items-center justify-center my-auto text-center space-y-8 animate-soft-pulse">
        <h2 className="font-lobster text-4xl md:text-6xl text-[#1f1610] tracking-wide leading-relaxed">
          Cười xinh để máy chuẩn bị chụp nè ...
        </h2>
        
        {/* Decorative flourish divider */}
        <div className="w-24 h-1 bg-[#c5a880]/60 rounded-full" />
      </div>
    </FavoriLayout>
  );
};
