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

  // Determine holes array from frameObj or fallback
  const holes = React.useMemo(() => {
    if (frameObj?.holes && frameObj.holes.length > 0) {
      return frameObj.holes;
    }
    if (effectiveLayout === '2x3') {
      return [
        { x: 106, y: 421, w: 1088, h: 880 },
        { x: 1204, y: 421, w: 1088, h: 880 },
        { x: 106, y: 1314, w: 1088, h: 880 },
        { x: 1204, y: 1314, w: 1088, h: 880 },
        { x: 106, y: 2208, w: 1088, h: 880 },
        { x: 1204, y: 2208, w: 1088, h: 880 },
      ];
    }
    if (effectiveLayout === '1x4') {
      return [
        { x: 545, y: 173, w: 1310, h: 833 },
        { x: 545, y: 1006, w: 1310, h: 794 },
        { x: 545, y: 1800, w: 1310, h: 797 },
        { x: 545, y: 2597, w: 1310, h: 830 },
      ];
    }
    // Default 2x2
    return [
      { x: 142, y: 792, w: 1016, h: 1186 },
      { x: 1242, y: 792, w: 1016, h: 1186 },
      { x: 142, y: 2057, w: 1016, h: 1186 },
      { x: 1242, y: 2057, w: 1016, h: 1186 },
    ];
  }, [frameObj, effectiveLayout]);

  // Determine how many slots the frame has
  const slotCount = React.useMemo(() => {
    if (holes.length > 0) return holes.length;
    if (effectiveLayout === '2x3') return 6;
    if (effectiveLayout === '1x2') return 2;
    return 4; // default 1x4 or 2x2
  }, [holes, effectiveLayout]);

  // Determine reference canvas dimensions from holes
  const canvasDimensions = React.useMemo(() => {
    let maxX = 0;
    let maxY = 0;
    holes.forEach((h) => {
      if (h.x + h.w > maxX) maxX = h.x + h.w;
      if (h.y + h.h > maxY) maxY = h.y + h.h;
    });
    // Standard CMS Frame PNG canvas is 2400 x 3600 (aspect 2:3)
    const w = maxX > 1500 ? 2400 : (maxX > 0 ? 1200 : 2400);
    const h = maxY > 2000 ? 3600 : (maxY > 0 ? 1800 : 3600);
    return { w, h };
  }, [holes]);

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

          {/* Miniature Frame Container - Absolute Hole Mapping with True Print Ratio */}
          <div 
            className={`rounded-2xl border-2 border-[#bfa282]/60 shadow-2xl relative overflow-hidden transition-all duration-300 ${
              isStrip
                ? 'w-[200px] h-[600px] aspect-[1/3]'
                : 'w-[340px] h-[510px] aspect-[2/3]'
            }`}
            style={{
              backgroundColor: frameObj?.bgColor || '#1a1a1a',
              aspectRatio: `${canvasDimensions.w} / ${canvasDimensions.h}`,
            }}
          >
            {/* Layer 1: Photo Slots (Dưới Frame Overlay) - Căn chỉnh chính xác từng % theo Holes */}
            {holes.map((hole, sIdx) => {
              if (sIdx >= slotCount) return null;
              const photo = slottedPhotos[sIdx];
              const leftPct = (hole.x / canvasDimensions.w) * 100;
              const topPct = (hole.y / canvasDimensions.h) * 100;
              const widthPct = (hole.w / canvasDimensions.w) * 100;
              const heightPct = (hole.h / canvasDimensions.h) * 100;

              return (
                <div
                  key={`photo-slot-${sIdx}`}
                  style={{
                    position: 'absolute',
                    left: `${leftPct}%`,
                    top: `${topPct}%`,
                    width: `${widthPct}%`,
                    height: `${heightPct}%`,
                  }}
                  className="overflow-hidden bg-neutral-900 z-10 flex items-center justify-center select-none"
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
                      Ô {sIdx + 1}
                    </div>
                  )}
                </div>
              );
            })}

            {/* Layer 2: Frame Overlay PNG - Khung viền đè lên trên, các lỗ trong suốt để lộ ảnh chụp */}
            {(frameObj?.frameUrl || frameObj?.thumbnailUrl) && (
              <img
                src={frameObj.frameUrl || frameObj.thumbnailUrl}
                alt="Frame Overlay"
                className="absolute inset-0 w-full h-full object-fill pointer-events-none z-20 select-none"
              />
            )}

            {/* Layer 3: Interactive Hitboxes & Badges - Cho phép tương tác chọn ô & hiển thị viền Active */}
            {holes.map((hole, sIdx) => {
              if (sIdx >= slotCount) return null;
              const isActive = activeSlot === sIdx;
              const leftPct = (hole.x / canvasDimensions.w) * 100;
              const topPct = (hole.y / canvasDimensions.h) * 100;
              const widthPct = (hole.w / canvasDimensions.w) * 100;
              const heightPct = (hole.h / canvasDimensions.h) * 100;

              return (
                <div
                  key={`hitbox-${sIdx}`}
                  onClick={() => setActiveSlot(sIdx)}
                  style={{
                    position: 'absolute',
                    left: `${leftPct}%`,
                    top: `${topPct}%`,
                    width: `${widthPct}%`,
                    height: `${heightPct}%`,
                  }}
                  className={`cursor-pointer rounded-sm transition-all duration-200 z-30 group ${
                    isActive
                      ? 'ring-4 ring-amber-400 shadow-2xl scale-[1.01]'
                      : 'hover:ring-2 hover:ring-[#7a0000]/60'
                  }`}
                >
                  {/* Slot Label Badge */}
                  <div
                    className={`absolute top-1.5 left-1.5 px-2 py-0.5 rounded-full text-[10px] font-bold tracking-wider backdrop-blur-md shadow z-30 ${
                      isActive
                        ? 'bg-amber-400 text-neutral-900 ring-2 ring-amber-500'
                        : 'bg-black/70 text-white'
                    }`}
                  >
                    Ô {sIdx + 1}
                  </div>

                  {/* Active Target Indicator Pulse */}
                  {isActive && (
                    <div className="absolute inset-0 border-2 border-amber-400 rounded-sm pointer-events-none animate-pulse z-30" />
                  )}
                </div>
              );
            })}
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
