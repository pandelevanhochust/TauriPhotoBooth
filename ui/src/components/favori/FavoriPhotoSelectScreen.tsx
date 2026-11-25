import React, { useEffect, useMemo, useRef, useState } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { FilteredImage } from './FilteredImage';
import { backendApi, type FilterOption, type FrameOption, type HoleRectDto, type LayoutOption } from '../../services/backendApi';
import { useCountdown } from '../../hooks/useCountdown';
import {
  IDENTITY_TRANSFORM,
  clampTransform,
  transformStyle,
  type PhotoTransform,
} from '../../services/photoTransform';

interface FavoriPhotoSelectScreenProps {
  photos: string[];
  frameObj?: FrameOption;
  layout?: LayoutOption;
  /** Bộ lọc đang chọn (màu CSS hoặc LUT). */
  filter?: FilterOption;
  timeoutSec?: number;
  /** Quay về bước chọn bộ lọc. */
  onBack?: () => void;
  /** Nhận ảnh theo đúng thứ tự ô kèm phóng to / độ lệch của từng ô, rồi chuyển bước. */
  onConfirm: (slottedPhotos: string[], transforms: PhotoTransform[]) => void;
}

// Dùng khi frame không có toạ độ ô: chia đều theo số ô của layout trên canvas 2400x3600
function fallbackHoles(layout?: LayoutOption): HoleRectDto[] {
  if (layout?.id === '1x4') {
    return [0, 1, 2, 3].map((i) => ({ x: 545, y: 173 + i * 815, w: 1310, h: 795 }));
  }
  return [
    { x: 142, y: 792, w: 1016, h: 1186 },
    { x: 1242, y: 792, w: 1016, h: 1186 },
    { x: 142, y: 2057, w: 1016, h: 1186 },
    { x: 1242, y: 2057, w: 1016, h: 1186 },
  ];
}

const DEFAULT_PHOTO_ASPECT = 3 / 2;
const TAP_SLOP_PX = 6;

interface Gesture {
  slot: number;
  pointers: Map<number, { x: number; y: number }>;
  start: PhotoTransform;
  startCentroid: { x: number; y: number };
  startDist: number;
  rect: DOMRect;
  /** Đã kéo quá ngưỡng chạm: không còn là một cú chạm. */
  moved: boolean;
  /** Đã có từ 2 ngón: chụm / phóng to chứ không phải chạm để gỡ ảnh. */
  multi: boolean;
}

function centroidAndDist(pointers: Map<number, { x: number; y: number }>) {
  const pts = [...pointers.values()];
  const cx = pts.reduce((s, p) => s + p.x, 0) / pts.length;
  const cy = pts.reduce((s, p) => s + p.y, 0) / pts.length;
  const dist = pts.length >= 2 ? Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y) : 0;
  return { centroid: { x: cx, y: cy }, dist };
}

export const FavoriPhotoSelectScreen: React.FC<FavoriPhotoSelectScreenProps> = ({
  photos,
  frameObj,
  layout,
  filter,
  timeoutSec = 180,
  onBack,
  onConfirm,
}) => {
  const holes = useMemo<HoleRectDto[]>(
    () => (frameObj?.holes && frameObj.holes.length > 0 ? frameObj.holes : fallbackHoles(layout)),
    [frameObj, layout],
  );
  const slotCount = holes.length;

  // Frame đã khoét ô tô màu (từ host); chưa có thì tạm dùng frame gốc
  const [overlayUrl, setOverlayUrl] = useState<string | undefined>(undefined);
  useEffect(() => {
    let active = true;
    setOverlayUrl(undefined);
    if (frameObj) {
      backendApi.getFrameOverlay(frameObj).then((url) => {
        if (active) setOverlayUrl(url);
      });
    }
    return () => {
      active = false;
    };
  }, [frameObj]);
  const frameSrc = overlayUrl || frameObj?.frameUrl || frameObj?.thumbnailUrl;

  // Kích thước canvas của frame PNG: ưu tiên kích thước ảnh thật, sau đó tới layout
  const [frameSize, setFrameSize] = useState<{ w: number; h: number } | null>(null);
  const canvas = useMemo(() => {
    const maxX = Math.max(...holes.map((h) => h.x + h.w));
    const maxY = Math.max(...holes.map((h) => h.y + h.h));
    const candidates = [frameSize, layout ? { w: layout.widthPx, h: layout.heightPx } : null];
    // Toạ độ ô phải nằm gọn trong canvas, nếu không thì kích thước đó không cùng hệ toạ độ với ô
    const fit = candidates.find((c) => c && c.w >= maxX && c.h >= maxY);
    return fit || { w: Math.max(maxX, 1), h: Math.max(maxY, 1) };
  }, [holes, frameSize, layout]);

  // slots[i] = chỉ số ảnh trong `photos` đang nằm ở ô i, hoặc null nếu ô trống
  const [slots, setSlots] = useState<(number | null)[]>(() => Array(slotCount).fill(null));
  const [transforms, setTransforms] = useState<PhotoTransform[]>(() => Array(slotCount).fill(IDENTITY_TRANSFORM));
  const [photoAspects, setPhotoAspects] = useState<Record<number, number>>({});

  const slotsRef = useRef(slots);
  slotsRef.current = slots;
  const transformsRef = useRef(transforms);
  transformsRef.current = transforms;
  const gestureRef = useRef<Gesture | null>(null);

  const photoAspect = (photoIndex: number | null) =>
    (photoIndex !== null && photoAspects[photoIndex]) || DEFAULT_PHOTO_ASPECT;
  const holeAspect = (sIdx: number) => (holes[sIdx].h > 0 ? holes[sIdx].w / holes[sIdx].h : 1);

  const rememberAspect = (photoIndex: number, img: HTMLImageElement) => {
    if (img.naturalWidth > 0 && img.naturalHeight > 0 && !photoAspects[photoIndex]) {
      const aspect = img.naturalWidth / img.naturalHeight;
      setPhotoAspects((prev) => (prev[photoIndex] ? prev : { ...prev, [photoIndex]: aspect }));
    }
  };

  const setSlotTransform = (sIdx: number, next: PhotoTransform) => {
    const clamped = clampTransform(next, photoAspect(slotsRef.current[sIdx]), holeAspect(sIdx));
    setTransforms((prev) => prev.map((t, i) => (i === sIdx ? clamped : t)));
  };

  const resetSlotTransform = (sIdx: number) =>
    setTransforms((prev) => prev.map((t, i) => (i === sIdx ? IDENTITY_TRANSFORM : t)));

  const isComplete = slots.every((s) => s !== null);

  const handlePickPhoto = (photoIndex: number) => {
    // Một ảnh có thể đặt vào nhiều ô: chạm là đưa vào ô trống đầu tiên (gỡ ảnh bằng cách chạm vào ảnh trong khung)
    const prev = slotsRef.current;
    const empty = prev.indexOf(null);
    if (empty === -1) return;
    setSlots(prev.map((s, i) => (i === empty ? photoIndex : s)));
    resetSlotTransform(empty);
  };

  const handleClearSlot = (slotIndex: number) => {
    setSlots((prev) => prev.map((s, i) => (i === slotIndex ? null : s)));
    resetSlotTransform(slotIndex);
  };

  const confirm = (current: (number | null)[]) => {
    if (photos.length === 0) {
      onConfirm([], []);
      return;
    }
    // Ô còn trống được điền bằng các ảnh chưa dùng theo thứ tự chụp
    const used = new Set(current.filter((s): s is number => s !== null));
    const unused = photos.map((_, i) => i).filter((i) => !used.has(i));
    let cursor = 0;
    const filled = current.map((s) => {
      if (s !== null) return s;
      const next = unused.length > 0 ? unused[cursor % unused.length] : cursor % photos.length;
      cursor++;
      return next;
    });
    const finalTransforms = filled.map((pIdx, sIdx) =>
      current[sIdx] === null
        ? IDENTITY_TRANSFORM
        : clampTransform(transformsRef.current[sIdx] || IDENTITY_TRANSFORM, photoAspect(pIdx), holeAspect(sIdx)),
    );
    onConfirm(
      filled.map((i) => photos[i]),
      finalTransforms,
    );
  };

  const timeLeft = useCountdown(timeoutSec, () => confirm(slotsRef.current));

  // --- Kéo bằng 1 ngón, phóng to/thu nhỏ bằng 2 ngón ---
  const beginGesture = (sIdx: number, el: HTMLElement, pointers: Map<number, { x: number; y: number }>) => {
    const { centroid, dist } = centroidAndDist(pointers);
    const prev = gestureRef.current && gestureRef.current.slot === sIdx ? gestureRef.current : null;
    gestureRef.current = {
      slot: sIdx,
      pointers,
      start: transformsRef.current[sIdx] || IDENTITY_TRANSFORM,
      startCentroid: centroid,
      startDist: dist,
      rect: el.getBoundingClientRect(),
      // Giữ lại để biết cả thao tác (nhấc / đặt ngón giữa chừng) có phải một cú chạm đơn không
      moved: prev?.moved ?? false,
      multi: (prev?.multi ?? false) || pointers.size >= 2,
    };
  };

  const handlePointerDown = (sIdx: number) => (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    const g = gestureRef.current;
    const pointers = g && g.slot === sIdx ? g.pointers : new Map<number, { x: number; y: number }>();
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    beginGesture(sIdx, e.currentTarget, pointers);
  };

  const handlePointerMove = (sIdx: number) => (e: React.PointerEvent<HTMLDivElement>) => {
    const g = gestureRef.current;
    if (!g || g.slot !== sIdx || !g.pointers.has(e.pointerId)) return;
    g.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const { centroid, dist } = centroidAndDist(g.pointers);
    const dx = centroid.x - g.startCentroid.x;
    const dy = centroid.y - g.startCentroid.y;
    if (g.pointers.size >= 2 || Math.hypot(dx, dy) >= TAP_SLOP_PX) g.moved = true;
    if (!g.moved) return;
    const scale = g.pointers.size >= 2 && g.startDist > 0 ? g.start.scale * (dist / g.startDist) : g.start.scale;
    setSlotTransform(sIdx, {
      scale,
      x: g.start.x + dx / Math.max(1, g.rect.width),
      y: g.start.y + dy / Math.max(1, g.rect.height),
    });
  };

  const handlePointerEnd = (sIdx: number) => (e: React.PointerEvent<HTMLDivElement>) => {
    const g = gestureRef.current;
    if (!g || g.slot !== sIdx) return;
    g.pointers.delete(e.pointerId);
    if (g.pointers.size === 0) {
      // Chạm nhẹ (không kéo, không chụm) vào ảnh trong khung: gỡ ảnh ra khỏi khung
      const isTap = e.type === 'pointerup' && !g.moved && !g.multi;
      gestureRef.current = null;
      if (isTap) handleClearSlot(sIdx);
    } else {
      // Nhấc 1 trong 2 ngón: tiếp tục kéo bằng ngón còn lại từ vị trí hiện tại
      beginGesture(sIdx, e.currentTarget, g.pointers);
    }
  };

  const handleWheel = (sIdx: number) => (e: React.WheelEvent<HTMLDivElement>) => {
    const cur = transformsRef.current[sIdx] || IDENTITY_TRANSFORM;
    setSlotTransform(sIdx, { ...cur, scale: cur.scale * Math.exp(-e.deltaY * 0.0015) });
  };

  const holeBox = (hole: HoleRectDto) => ({
    left: `${(hole.x / canvas.w) * 100}%`,
    top: `${(hole.y / canvas.h) * 100}%`,
    width: `${(hole.w / canvas.w) * 100}%`,
    height: `${(hole.h / canvas.h) * 100}%`,
  });

  return (
    <FavoriLayout
      stepNumber={9}
      stepTitle="Chọn ảnh để in"
      onBack={onBack}
      onNext={() => confirm(slots)}
      nextLabel="In ảnh"
      disableNext={!isComplete}
      timeLeft={timeLeft}
      wide
    >
      <div className="flex items-stretch justify-center gap-20 w-full h-full max-h-full px-8">
        {/* Kho ảnh vừa chụp: ảnh trần, 2 cột */}
        <div className="w-[46%] max-w-[760px] overflow-y-auto pr-2">
          <div className="grid grid-cols-2 gap-3">
            {photos.map((photo, pIdx) => {
              return (
                <button
                  key={pIdx}
                  type="button"
                  onClick={() => handlePickPhoto(pIdx)}
                  aria-label={`Ảnh ${pIdx + 1}`}
                  className="relative block w-full aspect-[3/2] p-0 border-0 bg-neutral-900 overflow-hidden cursor-pointer active:scale-[0.98] transition-transform duration-150"
                >
                  <FilteredImage
                    src={photo}
                    filter={filter}
                    alt=""
                    onLoad={(e) => rememberAspect(pIdx, e.currentTarget)}
                    className="w-full h-full object-cover"
                  />
                </button>
              );
            })}
          </div>
        </div>

        {/* Frame xem trước: ảnh nằm DƯỚI frame, giống bản in */}
        <div className="flex flex-col items-center flex-shrink-0 h-full">
          <div
            className="relative h-full bg-white overflow-hidden"
            style={{ aspectRatio: `${canvas.w} / ${canvas.h}` }}
          >
            {holes.map((hole, sIdx) => {
              const photoIndex = slots[sIdx];
              const photo = photoIndex !== null ? photos[photoIndex] : undefined;
              const t = clampTransform(transforms[sIdx] || IDENTITY_TRANSFORM, photoAspect(photoIndex), holeAspect(sIdx));
              return (
                <div key={`slot-${sIdx}`} style={holeBox(hole)} className="absolute z-10 overflow-hidden bg-white">
                  {photo && (
                    <FilteredImage
                      src={photo}
                      filter={filter}
                      alt=""
                      draggable={false}
                      onLoad={(e) => photoIndex !== null && rememberAspect(photoIndex, e.currentTarget)}
                      className="absolute max-w-none pointer-events-none select-none"
                      style={transformStyle(t, photoAspect(photoIndex), holeAspect(sIdx))}
                    />
                  )}
                </div>
              );
            })}

            {frameSrc && (
              <img
                src={frameSrc}
                alt=""
                draggable={false}
                onLoad={(e) => {
                  const img = e.currentTarget;
                  if (img.naturalWidth > 0) setFrameSize({ w: img.naturalWidth, h: img.naturalHeight });
                }}
                className="absolute inset-0 w-full h-full object-fill pointer-events-none z-20 select-none"
              />
            )}

            {/* Vùng chạm nằm trên cùng: kéo / phóng to ảnh trong ô */}
            {holes.map((hole, sIdx) =>
              slots[sIdx] !== null ? (
                <div
                  key={`hit-${sIdx}`}
                  role="button"
                  aria-label={`Chỉnh ảnh trong ô ${sIdx + 1}`}
                  style={{ ...holeBox(hole), touchAction: 'none' }}
                  onPointerDown={handlePointerDown(sIdx)}
                  onPointerMove={handlePointerMove(sIdx)}
                  onPointerUp={handlePointerEnd(sIdx)}
                  onPointerCancel={handlePointerEnd(sIdx)}
                  onWheel={handleWheel(sIdx)}
                  className="absolute z-30 cursor-grab active:cursor-grabbing"
                />
              ) : null,
            )}
          </div>

        </div>
      </div>
    </FavoriLayout>
  );
};
