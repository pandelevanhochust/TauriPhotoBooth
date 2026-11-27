import React, { useState, useEffect, useRef } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2 } from 'lucide-react';
import { backendApi, type FilterOption } from '../../services/backendApi';
import { useCountdown } from '../../hooks/useCountdown';
import { FilteredImage } from './FilteredImage';
import { ipc } from '../../services/ipc';
import { warmLutPreviews } from '../../services/lut';
import { SKIN_MAX, SKIN_STEP, type SkinLevel } from '../../services/skin';

export type { FilterOption };

interface FavoriFilterScreenProps {
  photos: string[];
  selectedFilter: string;
  onSelectFilter: (id: string, filterObj?: FilterOption) => void;
  timeoutSec?: number;
  onNext: () => void;
  /** Thử nghiệm: hiện thanh kéo độ làm mịn da 0-100 (bật trong Admin). */
  skinEnabled?: boolean;
  skin?: SkinLevel;
  onSelectSkin?: (level: SkinLevel) => void;
}

/** Nhãn ngắn cho nút tròn: "Ảnh Gốc (Natural)" -> "Gốc", "Đen Trắng Cổ Điển (Classic B&W)" -> "Đen Trắng Cổ Điển". */
function shortLabel(filter: FilterOption): string {
  if (filter.code === 'original') return 'Gốc';
  return filter.name.replace(/\s*\(.*\)\s*$/, '').trim() || filter.name;
}

export const FavoriFilterScreen: React.FC<FavoriFilterScreenProps> = ({
  photos,
  selectedFilter,
  onSelectFilter,
  timeoutSec = 60,
  onNext,
  skinEnabled = false,
  skin = 0,
  onSelectSkin,
}) => {
  const [filters, setFilters] = useState<FilterOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  // Ảnh đang hiện lớn; khách chạm ảnh nhỏ bên dưới để đổi
  const [mainIndex, setMainIndex] = useState<number>(0);

  // Thanh kéo độ làm mịn: số hiển thị đổi ngay khi kéo, còn ảnh chỉ tính lại khi dừng kéo (tránh tính liên tục từng nấc)
  const [skinDraft, setSkinDraft] = useState<number>(skin);
  const skinDraftRef = useRef(skin);
  const skinTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const flushSkin = () => {
    if (skinTimerRef.current) {
      clearTimeout(skinTimerRef.current);
      skinTimerRef.current = null;
    }
    if (skinDraftRef.current !== skin) onSelectSkin?.(skinDraftRef.current);
  };
  const changeSkin = (value: number) => {
    skinDraftRef.current = value;
    setSkinDraft(value);
    if (skinTimerRef.current) clearTimeout(skinTimerRef.current);
    skinTimerRef.current = setTimeout(flushSkin, 300);
  };
  // Sang bước sau (bấm Tiếp tục hoặc hết giờ): chốt giá trị đang kéo dở để bước 9 dùng đúng độ mịn khách thấy
  const handleNext = () => {
    flushSkin();
    onNext();
  };
  useEffect(
    () => () => {
      if (skinTimerRef.current) clearTimeout(skinTimerRef.current);
    },
    [],
  );

  const timeLeft = useCountdown(timeoutSec, handleNext);

  // Bộ lọc đang chọn, đọc trong callback của sự kiện mà không phải đăng ký lại
  const selectedRef = useRef(selectedFilter);
  selectedRef.current = selectedFilter;

  useEffect(() => {
    let isMounted = true;
    const load = () =>
      backendApi.getPresets().then((data) => {
        if (!isMounted) return;
        setFilters(data);
        setLoading(false);
        // Luôn truyền filterObj lên App (kể cả khi id đã khớp) để bước sau có cssFilter mới nhất
        const sel = selectedRef.current;
        const current = data.find((f) => f.id === sel || f.code === sel) || data[0];
        if (current) onSelectFilter(current.id, current);
      });

    // Host trả ngay bản đã lưu rồi hỏi server ngầm; server có thay đổi thì tải lại danh sách
    const unlisten = ipc.listen<{ key?: string }>('cms_updated', (data) => {
      if (data?.key === 'presets') load();
    });
    load();

    return () => {
      isMounted = false;
      unlisten();
    };
  }, []);

  // Tính sẵn ảnh đã áp các bộ lọc LUT (bộ lọc đang chọn trước) để khách chạm là đổi màu ngay
  useEffect(() => {
    if (filters.length === 0 || photos.length === 0) return;
    const sel = selectedRef.current;
    const ordered = [...filters].sort((a, b) => Number(b.id === sel || b.code === sel) - Number(a.id === sel || a.code === sel));
    return warmLutPreviews(photos, ordered);
  }, [filters, photos]);

  const activeFilter = filters.find((f) => f.id === selectedFilter || f.code === selectedFilter);

  return (
    <FavoriLayout stepNumber={8} stepTitle="Chọn bộ lọc" onNext={handleNext} timeLeft={timeLeft} wide>
      <div className="flex items-stretch justify-center gap-20 w-full h-full max-h-full px-8">
        {/* Một ảnh lớn áp bộ lọc đang chọn; các ảnh đã chụp xếp thành hàng ảnh nhỏ bên dưới, chạm để đổi ảnh lớn */}
        <div className="flex-1 max-w-[1000px] min-w-0 min-h-0 flex flex-col gap-4">
          {photos.length > 0 ? (
            <>
              {/* Ảnh lớn: lớn nhất có thể trong phần còn lại, giữ tỉ lệ 3:2 */}
              <div className="flex-1 min-h-0 flex items-center justify-center" style={{ containerType: 'size' }}>
                <div
                  className="relative bg-neutral-900 overflow-hidden shadow-lg"
                  style={{ aspectRatio: '3 / 2', width: 'min(100cqw, calc(100cqh * 1.5))' }}
                >
                  <FilteredImage
                    src={photos[Math.min(mainIndex, photos.length - 1)]}
                    filter={activeFilter}
                    skin={skin}
                    alt={`Ảnh ${Math.min(mainIndex, photos.length - 1) + 1}`}
                    className="w-full h-full object-cover transition-[filter] duration-300"
                  />
                </div>
              </div>

              {/* Ảnh nhỏ: cuộn ngang khi nhiều ảnh */}
              {photos.length > 1 && (
                <div className="flex-shrink-0 flex gap-3 overflow-x-auto px-1 py-2 justify-center-safe">
                  {photos.map((photo, pIdx) => {
                    const isMain = pIdx === Math.min(mainIndex, photos.length - 1);
                    return (
                      <button
                        key={pIdx}
                        type="button"
                        onClick={() => setMainIndex(pIdx)}
                        aria-pressed={isMain}
                        aria-label={`Ảnh ${pIdx + 1}`}
                        className={`relative flex-shrink-0 h-[6.5rem] aspect-[3/2] p-0 bg-neutral-900 overflow-hidden cursor-pointer transition-all duration-200 active:scale-95 border-[3px] ${
                          isMain ? 'border-[#b00012] shadow-lg' : 'border-transparent opacity-75'
                        }`}
                      >
                        {/* Ảnh nhỏ chỉ cần màu của bộ lọc: làm mịn da ở cỡ này không thấy được nên bỏ qua cho đỡ tính */}
                        <FilteredImage
                          src={photo}
                          filter={activeFilter}
                          alt=""
                          draggable={false}
                          className="w-full h-full object-cover pointer-events-none select-none"
                        />
                      </button>
                    );
                  })}
                </div>
              )}
            </>
          ) : (
            <div className="w-full aspect-[3/2] bg-neutral-900 flex items-center justify-center text-neutral-400 text-sm">
              Chưa có ảnh
            </div>
          )}
        </div>

        {/* Cột bên phải: (thử nghiệm) làm mịn da, rồi lưới nút lọc tròn */}
        <div className="w-[26rem] flex-shrink-0 max-h-full flex flex-col gap-6 py-2 self-center">
          {skinEnabled && (
            <div className="flex flex-col gap-2 px-6">
              <div className="flex items-baseline justify-between">
                <span className="font-lobster text-xl text-[#1f1610]">Làm mịn da</span>
                <span className="text-lg font-bold tabular-nums text-[#7a0000]">{skinDraft}</span>
              </div>
              <input
                type="range"
                min={0}
                max={SKIN_MAX}
                step={SKIN_STEP}
                value={skinDraft}
                onChange={(e) => changeSkin(Number(e.target.value))}
                onPointerUp={flushSkin}
                onKeyUp={flushSkin}
                aria-label="Độ làm mịn da"
                className="skin-slider w-full cursor-pointer"
              />
              <div className="flex justify-between text-xs font-semibold text-[#6b5b52]">
                <span>0</span>
                <span>{SKIN_MAX}</span>
              </div>
            </div>
          )}
          <div className="min-h-0 overflow-y-auto">
          {loading ? (
            <div className="flex flex-col items-center justify-center py-16 space-y-3">
              <Loader2 className="w-8 h-8 animate-spin text-[#7a0000]" />
              <span className="text-[#6b5b52] font-medium">Đang tải bộ lọc...</span>
            </div>
          ) : (
            <div className="grid grid-cols-3 gap-x-6 gap-y-8 justify-items-center">
              {filters.map((f) => {
                const isSelected = activeFilter?.id === f.id;
                return (
                  <button
                    key={f.id}
                    type="button"
                    onClick={() => onSelectFilter(f.id, f)}
                    aria-pressed={isSelected}
                    title={f.name}
                    className={`w-[7rem] h-[7rem] rounded-full border-2 border-[#7a0000] flex items-center justify-center px-2 text-center text-sm font-bold leading-tight cursor-pointer transition-all duration-200 active:scale-95 ${
                      isSelected
                        ? 'bg-[#b00012] border-[#b00012] text-white shadow-lg'
                        : 'bg-white/70 text-[#7a0000]'
                    }`}
                  >
                    {shortLabel(f)}
                  </button>
                );
              })}
            </div>
          )}
          </div>
        </div>
      </div>
    </FavoriLayout>
  );
};
