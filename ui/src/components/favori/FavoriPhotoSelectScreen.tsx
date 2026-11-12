import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Check, Sparkles, RefreshCw, ArrowLeftRight } from 'lucide-react';
import type { FrameOption } from '../../services/backendApi';

interface FavoriPhotoSelectScreenProps {
  photos: string[];
  frameObj?: FrameOption;
  layoutType?: string;
  filterCss?: string;
  onConfirmSelection?: (slottedPhotos: string[]) => void;
  maxRetakes?: number;
  onRetake?: (index: number) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriPhotoSelectScreen: React.FC<FavoriPhotoSelectScreenProps> = ({
  photos,
  frameObj,
  layoutType = 'strip2x6',
  filterCss,
  onConfirmSelection,
  onNext,
  onBack,
}) => {
  // Determine effective layout from frameObj or layoutType
  const effectiveLayout = React.useMemo(() => {
    if (frameObj?.layoutId) return frameObj.layoutId;
    const l = (layoutType || '').toLowerCase();
    if (l === 'postcard4x6' || l === '2x2') return '2x2';
    if (l === 'special4x6' || l === '2x3') return '2x3';
    return '1x4';
  }, [frameObj, layoutType]);

  // Determine how many slots the frame has
  const slotCount = React.useMemo(() => {
    if (frameObj?.holes && frameObj.holes.length > 0) {
      return frameObj.holes.length;
    }
    if (effectiveLayout === '2x3') return 6;
    if (effectiveLayout === '1x2') return 2;
    return 4; // default 1x4 or 2x2
  }, [frameObj, effectiveLayout]);

  const isGrid2x2 = effectiveLayout === '2x2';
  const isGrid2x3 = effectiveLayout === '2x3';
  const isStrip = effectiveLayout === '1x4';

  // Slotted photos state: array of length `slotCount`
  const [slottedPhotos, setSlottedPhotos] = useState<string[]>(() => {
    const initial: string[] = [];
    for (let i = 0; i < slotCount; i++) {
      if (photos[i]) {
        initial.push(photos[i]);
      } else if (photos.length > 0) {
        initial.push(photos[i % photos.length]);
      } else {
        initial.push('');
      }
    }
    return initial;
  });

  // Active slot index that user is assigning to
  const [activeSlot, setActiveSlot] = useState<number>(0);

  // Sync if photos change
  useEffect(() => {
    if (photos.length > 0) {
      setSlottedPhotos((prev) => {
        const next = [...prev];
        for (let i = 0; i < slotCount; i++) {
          if (!next[i] && photos[i]) {
            next[i] = photos[i];
          }
        }
        return next;
      });
    }
  }, [photos, slotCount]);

  // Handle clicking on a photo in the pool
  const handleSelectPoolPhoto = (photo: string) => {
    const updated = [...slottedPhotos];
    updated[activeSlot] = photo;
    setSlottedPhotos(updated);

    // Auto advance to the next slot for quick assignment
    setActiveSlot((prev) => (prev + 1) % slotCount);
  };

  // Reset to sequential fill (Shot 1, Shot 2, Shot 3...)
  const handleAutoFill = () => {
    const reset: string[] = [];
    for (let i = 0; i < slotCount; i++) {
      reset.push(photos[i % photos.length] || '');
    }
    setSlottedPhotos(reset);
    setActiveSlot(0);
  };

  // Swap / Rotate slot contents
  const handleRotateSlots = () => {
    if (slottedPhotos.length < 2) return;
    const rotated = [slottedPhotos[slottedPhotos.length - 1], ...slottedPhotos.slice(0, slottedPhotos.length - 1)];
    setSlottedPhotos(rotated);
  };

  // Proceed to next step with confirmed photos
  const handleProceed = () => {
    if (onConfirmSelection) {
      onConfirmSelection(slottedPhotos);
    }
    onNext();
  };

  return (
    <FavoriLayout
      stepNumber={9}
      stepTitle="Chọn ảnh vào khung in"
      onBack={onBack}
      onNext={handleProceed}
    >
      <div className="flex flex-col lg:flex-row items-center lg:items-start justify-center w-full max-w-6xl py-2 gap-8">
        {/* Left Column: Live Frame Assembly Preview */}
        <div className="flex flex-col items-center bg-white/80 backdrop-blur-md rounded-3xl p-5 shadow-xl border border-[#bfa282]/40 flex-shrink-0">
          <div className="flex items-center justify-between w-full mb-2 px-1">
            <span className="font-lobster text-xl text-[#7a0000]">
              Mô phỏng Khung in
            </span>
            <span className="text-xs bg-[#7a0000]/10 text-[#7a0000] font-semibold px-2.5 py-1 rounded-full">
              {slotCount} ô ảnh • {effectiveLayout}
            </span>
          </div>

          <p className="text-xs text-[#6b5b52] text-center mb-3">
            Chạm vào ô bên dưới, sau đó chọn ảnh bên phải để gán
          </p>

          {/* Miniature Frame Container - True Print Aspect Ratio */}
          <div 
            className={`rounded-2xl border-2 border-[#bfa282]/60 shadow-xl flex items-center justify-center relative overflow-hidden transition-all duration-300 ${
              isStrip
                ? 'w-[190px] h-[570px] aspect-[1/3]'
                : 'w-[340px] h-[510px] aspect-[2/3]'
            }`}
            style={{
              backgroundColor: frameObj?.bgColor || '#fcfbf8',
            }}
          >
            {/* Layer 1: Photo Slots Grid */}
            <div
              className={`w-full h-full z-0 ${
                isGrid2x2
                  ? 'grid grid-cols-2 gap-3 px-3.5 py-8 content-center justify-center'
                  : isGrid2x3
                  ? 'grid grid-cols-2 gap-2 px-3 py-6 content-center justify-center'
                  : 'flex flex-col justify-between py-6 px-3'
              }`}
            >
              {slottedPhotos.map((photo, sIdx) => {
                const isActive = activeSlot === sIdx;
                return (
                  <div
                    key={sIdx}
                    onClick={() => setActiveSlot(sIdx)}
                    className={`relative rounded-xl overflow-hidden cursor-pointer transition-all duration-200 border-2 bg-neutral-900 group aspect-[4/3] ${
                      isActive
                        ? 'ring-4 ring-amber-400 border-amber-500 scale-[1.03] shadow-xl z-20'
                        : 'border-neutral-300 hover:border-[#7a0000]/60 z-0'
                    }`}
                  >
                    {photo ? (
                      <img
                        src={photo}
                        alt={`Slot ${sIdx + 1}`}
                        className="w-full h-full object-cover object-center pointer-events-none"
                        style={{ filter: filterCss || 'none' }}
                      />
                    ) : (
                      <div className="w-full h-full flex items-center justify-center text-neutral-400 text-xs font-medium">
                        Ô {sIdx + 1} trống
                      </div>
                    )}

                    {/* Slot Label Badge */}
                    <div
                      className={`absolute top-1.5 left-1.5 px-2 py-0.5 rounded-full text-[10px] font-bold tracking-wider backdrop-blur-md shadow z-30 ${
                        isActive
                          ? 'bg-amber-400 text-neutral-900'
                          : 'bg-black/60 text-white'
                      }`}
                    >
                      Ô {sIdx + 1}
                    </div>

                    {/* Active Target Indicator Pulse */}
                    {isActive && (
                      <div className="absolute inset-0 border-2 border-amber-400 rounded-xl pointer-events-none animate-pulse z-30" />
                    )}
                  </div>
                );
              })}
            </div>

            {/* Layer 2: Frame Overlay */}
            {(frameObj?.frameUrl || frameObj?.thumbnailUrl) && (
              <img
                src={frameObj.frameUrl || frameObj.thumbnailUrl}
                alt="Frame Overlay"
                className="absolute inset-0 w-full h-full object-contain pointer-events-none z-10 select-none opacity-80"
              />
            )}
          </div>

          {/* Quick Action Tools */}
          <div className="flex items-center justify-center gap-3 w-full mt-4">
            <button
              onClick={handleAutoFill}
              className="px-3.5 py-1.5 rounded-full text-xs font-semibold bg-neutral-100 hover:bg-neutral-200 text-[#1f1610] flex items-center gap-1.5 transition-all shadow-sm cursor-pointer"
              title="Tự động điền ảnh theo thứ tự chụp"
            >
              <RefreshCw className="w-3.5 h-3.5 text-[#7a0000]" />
              Điền theo thứ tự
            </button>
            <button
              onClick={handleRotateSlots}
              className="px-3.5 py-1.5 rounded-full text-xs font-semibold bg-neutral-100 hover:bg-neutral-200 text-[#1f1610] flex items-center gap-1.5 transition-all shadow-sm cursor-pointer"
              title="Hoán đổi vòng quanh các ô"
            >
              <ArrowLeftRight className="w-3.5 h-3.5 text-[#7a0000]" />
              Đổi vị trí
            </button>
          </div>
        </div>

        {/* Right Column: Photo Pool (All Captured Photos) */}
        <div className="flex-1 flex flex-col items-center lg:items-start w-full">
          <div className="flex items-center justify-between w-full mb-3 px-1">
            <div>
              <h3 className="font-lobster text-2xl text-[#1f1610]">
                Kho ảnh vừa chụp
              </h3>
              <p className="text-xs text-[#6b5b52] mt-0.5">
                Đang gán vào: <strong className="text-[#7a0000]">Ô {activeSlot + 1}</strong> • Chạm vào ảnh bất kỳ để đưa vào ô này
              </p>
            </div>
            <span className="text-xs font-medium text-neutral-500 bg-neutral-100 px-3 py-1 rounded-full border border-neutral-200">
              {photos.length} kiểu ảnh
            </span>
          </div>

          {/* Photos Grid */}
          <div className="grid grid-cols-2 md:grid-cols-3 gap-4 w-full">
            {photos.map((photo, pIdx) => {
              // Find which slots this photo is currently placed in
              const assignedSlots: number[] = [];
              slottedPhotos.forEach((sp, idx) => {
                if (sp === photo) assignedSlots.push(idx + 1);
              });
              const isCurrentlyActiveInSlot = slottedPhotos[activeSlot] === photo;

              return (
                <div
                  key={pIdx}
                  onClick={() => handleSelectPoolPhoto(photo)}
                  className={`favori-card relative overflow-hidden flex flex-col p-2.5 cursor-pointer transition-all duration-200 ${
                    isCurrentlyActiveInSlot
                      ? 'ring-3 ring-amber-400 scale-[1.02] shadow-xl'
                      : 'hover:scale-[1.01] hover:border-[#7a0000]/60'
                  }`}
                >
                  {/* Photo thumbnail */}
                  <div className="w-full aspect-[4/3] rounded-lg overflow-hidden bg-neutral-900 relative">
                    <img
                      src={photo}
                      alt={`Shot ${pIdx + 1}`}
                      className="w-full h-full object-cover"
                      style={{ filter: filterCss || 'none' }}
                      loading="lazy"
                    />

                    {/* Assigned Slots Badges */}
                    {assignedSlots.length > 0 && (
                      <div className="absolute bottom-1.5 left-1.5 flex flex-wrap gap-1">
                        {assignedSlots.map((slotNum) => (
                          <span
                            key={slotNum}
                            className="bg-[#7a0000] text-white text-[10px] font-bold px-1.5 py-0.5 rounded shadow"
                          >
                            Ô {slotNum}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>

                  {/* Card Label */}
                  <div className="flex items-center justify-between mt-2 px-1">
                    <span className="font-lobster text-base text-[#1f1610]">
                      Kiểu #{pIdx + 1}
                    </span>
                    <button
                      className={`text-xs px-2.5 py-0.5 rounded-full font-bold flex items-center gap-1 transition-all ${
                        isCurrentlyActiveInSlot
                          ? 'bg-amber-400 text-neutral-900'
                          : 'bg-[#7a0000]/10 text-[#7a0000] hover:bg-[#7a0000] hover:text-white'
                      }`}
                    >
                      {isCurrentlyActiveInSlot ? (
                        <>
                          <Check className="w-3 h-3" />
                          Đã chọn
                        </>
                      ) : (
                        <>
                          <Sparkles className="w-3 h-3" />
                          Gán vào Ô {activeSlot + 1}
                        </>
                      )}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

      </div>
    </FavoriLayout>
  );
};
