import React, { useState, useEffect, useRef } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Loader2 } from 'lucide-react';
import { backendApi, type FilterOption } from '../../services/backendApi';
import { useCountdown } from '../../hooks/useCountdown';
import { ipc } from '../../services/ipc';

export type { FilterOption };

interface FavoriFilterScreenProps {
  photos: string[];
  selectedFilter: string;
  onSelectFilter: (id: string, filterObj?: FilterOption) => void;
  timeoutSec?: number;
  onNext: () => void;
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
}) => {
  const [filters, setFilters] = useState<FilterOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  const timeLeft = useCountdown(timeoutSec, onNext);

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

  const activeFilter = filters.find((f) => f.id === selectedFilter || f.code === selectedFilter);
  const previewPhoto = photos[0];

  return (
    <FavoriLayout stepNumber={8} stepTitle="Chọn bộ lọc" onNext={onNext} timeLeft={timeLeft} wide>
      <div className="flex items-center justify-center gap-16 w-full h-full px-8">
        {/* Ảnh đầu tiên, áp bộ lọc đang chọn */}
        <div className="h-full max-h-[56vh] aspect-[3/2] bg-neutral-900 overflow-hidden flex-shrink min-w-0">
          {previewPhoto ? (
            <img
              src={previewPhoto}
              alt="Ảnh xem trước bộ lọc"
              className="w-full h-full object-cover transition-[filter] duration-300"
              style={{ filter: activeFilter?.cssFilter || 'none' }}
            />
          ) : (
            <div className="w-full h-full flex items-center justify-center text-neutral-400 text-sm">
              Chưa có ảnh
            </div>
          )}
        </div>

        {/* Lưới nút lọc tròn */}
        <div className="w-[420px] flex-shrink-0 max-h-[56vh] overflow-y-auto py-2">
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
                    className={`w-[112px] h-[112px] rounded-full border-2 border-[#7a0000] flex items-center justify-center px-2 text-center text-sm font-bold leading-tight cursor-pointer transition-all duration-200 active:scale-95 ${
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
    </FavoriLayout>
  );
};
