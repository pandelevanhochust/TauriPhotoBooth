import React, { useState } from 'react';
import { Sparkles, RefreshCw, Check, ArrowRight } from 'lucide-react';

interface ReviewScreenProps {
  photos: string[];
  maxRetakes?: number;
  onRetakePhoto: (indexToRetake: number) => void;
  onProceedToCompose: (beautifyEnabled: boolean) => void;
}

export const ReviewScreen: React.FC<ReviewScreenProps> = ({
  photos,
  maxRetakes = 1,
  onRetakePhoto,
  onProceedToCompose,
}) => {
  const [retakesLeft, setRetakesLeft] = useState(maxRetakes);
  const [selectedPhotoIndex, setSelectedPhotoIndex] = useState<number | null>(null);
  const [isBeautifyActive, setIsBeautifyActive] = useState(true);

  const handleRetakeClick = () => {
    if (selectedPhotoIndex === null || retakesLeft <= 0) return;
    setRetakesLeft(retakesLeft - 1);
    onRetakePhoto(selectedPhotoIndex);
  };

  return (
    <div className="relative w-screen h-screen bg-gradient-to-br from-[#0c0d14] via-[#151324] to-[#0c0d14] flex flex-col justify-between p-8 select-none overflow-hidden">
      {/* Top Header */}
      <div className="w-full flex justify-between items-center z-10">
        <div>
          <h2 className="text-3xl font-black text-white tracking-wide">XEM LẠI ẢNH ĐÃ CHỤP</h2>
          <p className="text-sm text-gray-400 mt-0.5">Chọn một tấm ảnh nếu bạn muốn chụp lại (Tối đa {maxRetakes} lần)</p>
        </div>

        {/* Retake Quota Badge */}
        <div className="flex items-center gap-3 bg-black/50 px-5 py-2.5 rounded-2xl border border-white/10 backdrop-blur-md">
          <RefreshCw className="w-4 h-4 text-rose-400" />
          <span className="text-sm text-gray-300 font-medium">
            Lượt chụp lại: <strong className={retakesLeft > 0 ? 'text-rose-400' : 'text-gray-500'}>{retakesLeft}</strong>
          </span>
        </div>
      </div>

      {/* Center 4-Photo Review Grid */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-6 max-w-6xl mx-auto w-full my-auto z-10">
        {photos.map((photo, index) => {
          const isSelected = selectedPhotoIndex === index;

          return (
            <div
              key={index}
              onClick={() => setSelectedPhotoIndex(isSelected ? null : index)}
              className={`relative group cursor-pointer aspect-[3/4] rounded-3xl overflow-hidden border-4 transition-all duration-200 shadow-2xl ${
                isSelected
                  ? 'border-rose-500 scale-105 shadow-[0_0_30px_rgba(244,63,94,0.5)]'
                  : 'border-white/10 hover:border-white/30'
              }`}
            >
              <img
                src={photo}
                alt={`Shot ${index + 1}`}
                className="w-full h-full object-cover transition duration-300 group-hover:scale-105"
              />

              {/* Number Badge */}
              <div className="absolute top-4 left-4 w-9 h-9 rounded-full bg-black/60 backdrop-blur-md border border-white/20 flex items-center justify-center font-bold text-sm text-white">
                {index + 1}
              </div>

              {/* Selected for retake indicator */}
              {isSelected && (
                <div className="absolute inset-0 bg-rose-500/20 backdrop-blur-[2px] flex items-center justify-center">
                  <div className="bg-rose-600 text-white font-bold text-sm px-4 py-2 rounded-full shadow-lg flex items-center gap-2">
                    <Check className="w-4 h-4" />
                    <span>Đã chọn</span>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Bottom Bar: Beautify Toggle & Actions */}
      <div className="w-full flex justify-between items-center z-10 max-w-6xl mx-auto">
        {/* Retake Button (Enabled only if 1 selected and retakes available) */}
        <div>
          {retakesLeft > 0 && selectedPhotoIndex !== null ? (
            <button
              onClick={handleRetakeClick}
              className="flex items-center gap-3 px-8 py-4 rounded-2xl bg-white/10 hover:bg-white/20 border border-white/20 text-white font-semibold text-lg active:scale-95 transition"
            >
              <RefreshCw className="w-5 h-5 text-rose-400" />
              <span>Chụp lại ảnh {selectedPhotoIndex + 1}</span>
            </button>
          ) : (
            <div className="text-gray-500 text-sm italic">
              {retakesLeft <= 0 ? 'Đã hết lượt chụp lại' : 'Chạm vào ảnh để chọn chụp lại'}
            </div>
          )}
        </div>

        {/* AI Beautify Filter & Finish Buttons */}
        <div className="flex items-center gap-5">
          {/* AI Beautify Toggle */}
          <button
            onClick={() => setIsBeautifyActive(!isBeautifyActive)}
            className={`flex items-center gap-3 px-6 py-4 rounded-2xl border transition active:scale-95 ${
              isBeautifyActive
                ? 'bg-gradient-to-r from-purple-500/30 to-pink-500/30 border-pink-400/60 text-pink-300 shadow-lg'
                : 'bg-black/40 border-white/10 text-gray-400'
            }`}
          >
            <Sparkles className="w-5 h-5 text-pink-400" />
            <span className="font-semibold text-base">
              {isBeautifyActive ? 'Làm Mịn Da AI: BẬT' : 'Làm Mịn Da AI: TẮT'}
            </span>
          </button>

          {/* Confirm & Proceed to Printing */}
          <button
            onClick={() => onProceedToCompose(isBeautifyActive)}
            className="flex items-center gap-4 px-10 py-4 rounded-2xl bg-gradient-to-r from-rose-500 via-purple-600 to-indigo-600 text-white font-bold text-xl tracking-wider shadow-2xl active:scale-95 transition"
          >
            <span>HOÀN TẤT & IN ẢNH</span>
            <ArrowRight className="w-6 h-6" />
          </button>
        </div>
      </div>
    </div>
  );
};
