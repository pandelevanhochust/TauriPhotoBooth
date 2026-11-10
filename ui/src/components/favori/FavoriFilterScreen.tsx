import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Sparkles, Loader2 } from 'lucide-react';
import { backendApi, type FilterOption } from '../../services/backendApi';

export type { FilterOption };

interface FavoriFilterScreenProps {
  photos: string[];
  selectedFilter: string;
  onSelectFilter: (id: string, filterObj?: FilterOption) => void;
  beautifyEnabled: boolean;
  onToggleBeautify: (val: boolean) => void;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriFilterScreen: React.FC<FavoriFilterScreenProps> = ({
  photos,
  selectedFilter,
  onSelectFilter,
  beautifyEnabled,
  onToggleBeautify,
  onNext,
  onBack,
}) => {
  const [filters, setFilters] = useState<FilterOption[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  const samplePhoto = photos[0] || 'https://picsum.photos/seed/sample/400/600';

  useEffect(() => {
    let isMounted = true;
    backendApi.getPresets().then((data) => {
      if (isMounted) {
        setFilters(data);
        setLoading(false);
        // Nếu filter hiện tại chưa được chọn hoặc không nằm trong danh sách, chọn item đầu tiên
        if (data.length > 0 && !data.some((f) => f.id === selectedFilter || f.code === selectedFilter)) {
          onSelectFilter(data[0].id, data[0]);
        }
      }
    });
    return () => {
      isMounted = false;
    };
  }, []);

  return (
    <FavoriLayout
      stepNumber={8}
      stepTitle="Chọn bộ lọc"
      onBack={onBack}
      onNext={onNext}
    >
      <div className="flex flex-col items-center w-full max-w-5xl py-2 space-y-6">
        {loading ? (
          <div className="flex flex-col items-center justify-center py-16 space-y-3">
            <Loader2 className="w-8 h-8 animate-spin text-[#7a0000]" />
            <span className="text-[#6b5b52] font-medium">Đang tải bộ lọc từ máy chủ...</span>
          </div>
        ) : (
          /* Filter Cards Row */
          <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-5 gap-4 w-full">
            {filters.map((f) => {
              const isSelected = selectedFilter === f.id || selectedFilter === f.code;
              return (
                <div
                  key={f.id}
                  onClick={() => onSelectFilter(f.id, f)}
                  className={`favori-card flex flex-col items-center p-3 cursor-pointer transition-all ${
                    isSelected
                      ? 'ring-3 ring-[#7a0000] scale-105 shadow-xl'
                      : 'hover:scale-102 hover:border-[#7a0000]/50'
                  }`}
                >
                  {/* Thumbnail Preview with Real-time Filter */}
                  <div className="w-full aspect-[3/4] rounded-lg overflow-hidden bg-neutral-200 mb-3 border border-neutral-300 shadow-inner">
                    <img
                      src={f.thumbnailUrl || samplePhoto}
                      alt={f.name}
                      className="w-full h-full object-cover transition-all duration-300"
                      style={{ filter: f.cssFilter }}
                    />
                  </div>

                  <span className="font-lobster text-lg text-[#1f1610] text-center leading-snug">
                    {f.name}
                  </span>

                  <div className="mt-2">
                    <div
                      className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${
                        isSelected ? 'border-[#7a0000] bg-[#7a0000]' : 'border-[#c5a880]'
                      }`}
                    >
                      {isSelected && <div className="w-1.5 h-1.5 rounded-full bg-white" />}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* AI Skin Beautification Feature Toggle */}
        <div className="w-full max-w-md bg-white/90 border-2 border-[#bfa282]/60 rounded-2xl p-4 flex items-center justify-between shadow-md">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-full bg-[#7a0000]/10 text-[#7a0000]">
              <Sparkles className="w-6 h-6" />
            </div>
            <div>
              <h4 className="font-lobster text-2xl text-[#1f1610]">
                Mịn da AI
              </h4>
              <p className="text-xs text-[#6b5b52]">
                Tự động làm sáng da và mịn màng tự nhiên
              </p>
            </div>
          </div>

          <button
            onClick={() => onToggleBeautify(!beautifyEnabled)}
            className={`w-14 h-8 flex items-center rounded-full p-1 cursor-pointer transition-colors duration-200 ease-in-out ${
              beautifyEnabled ? 'bg-[#7a0000]' : 'bg-neutral-300'
            }`}
          >
            <div
              className={`bg-white w-6 h-6 rounded-full shadow-md transform transition-transform duration-200 ease-in-out ${
                beautifyEnabled ? 'translate-x-6' : 'translate-x-0'
              }`}
            />
          </button>
        </div>
      </div>
    </FavoriLayout>
  );
};
