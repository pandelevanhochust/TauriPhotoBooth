import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';

export interface TabItem<T extends string> {
  id: T;
  label: string;
}

interface ScrollableTabsProps<T extends string> {
  tabs: TabItem<T>[];
  active: T;
  onChange: (id: T) => void;
}

const ARROW_STEP_PX = 220;
const DRAG_SLOP_PX = 6;

/**
 * Hàng tab cuộn ngang. Thanh cuộn bị ẩn toàn cục (index.css) nên có thêm:
 * lăn chuột, kéo bằng chuột, vuốt cảm ứng, mũi tên hai đầu khi còn tab bị che, và tự cuộn tới tab đang chọn.
 */
export function ScrollableTabs<T extends string>({ tabs, active, onChange }: ScrollableTabsProps<T>) {
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [canLeft, setCanLeft] = useState(false);
  const [canRight, setCanRight] = useState(false);
  const drag = useRef<{ startX: number; startScroll: number; moved: boolean } | null>(null);

  const updateArrows = useCallback(() => {
    const el = scrollerRef.current;
    if (!el) return;
    setCanLeft(el.scrollLeft > 2);
    setCanRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 2);
  }, []);

  useEffect(() => {
    updateArrows();
    const el = scrollerRef.current;
    if (!el) return;
    const observer = new ResizeObserver(updateArrows);
    observer.observe(el);
    return () => observer.disconnect();
  }, [updateArrows, tabs]);

  // Tab đang chọn luôn nằm trong vùng nhìn thấy
  useEffect(() => {
    const el = scrollerRef.current;
    const button = el?.querySelector<HTMLElement>(`[data-tab="${active}"]`);
    if (!el || !button) return;
    const left = button.offsetLeft - 32;
    const right = button.offsetLeft + button.offsetWidth + 32;
    if (left < el.scrollLeft) el.scrollTo({ left: Math.max(0, left), behavior: 'smooth' });
    else if (right > el.scrollLeft + el.clientWidth) el.scrollTo({ left: right - el.clientWidth, behavior: 'smooth' });
  }, [active]);

  const scrollBy = (delta: number) => scrollerRef.current?.scrollBy({ left: delta, behavior: 'smooth' });

  // Lăn chuột dọc thì trượt ngang (con lăn không có trục ngang)
  const handleWheel = (e: React.WheelEvent<HTMLDivElement>) => {
    const el = scrollerRef.current;
    if (!el || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    el.scrollLeft += e.deltaY;
  };

  // Kéo bằng chuột; cảm ứng đã tự vuốt được nên bỏ qua
  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.pointerType !== 'mouse' || e.button !== 0) return;
    drag.current = { startX: e.clientX, startScroll: scrollerRef.current?.scrollLeft ?? 0, moved: false };
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    const el = scrollerRef.current;
    if (!d || !el) return;
    const dx = e.clientX - d.startX;
    if (!d.moved && Math.abs(dx) < DRAG_SLOP_PX) return;
    d.moved = true;
    el.scrollLeft = d.startScroll - dx;
  };

  const endDrag = () => {
    // Giữ cờ "đã kéo" tới hết sự kiện click để cú kéo không bị tính là bấm chọn tab
    setTimeout(() => {
      drag.current = null;
    }, 0);
  };

  return (
    <div className="relative mb-6 border-b border-white/10 pb-3 flex-shrink-0">
      <div
        ref={scrollerRef}
        onScroll={updateArrows}
        onWheel={handleWheel}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={endDrag}
        onPointerLeave={endDrag}
        className="flex gap-3 overflow-x-auto cursor-grab active:cursor-grabbing"
      >
        {tabs.map((tab) => (
          <button
            key={tab.id}
            data-tab={tab.id}
            onClick={() => {
              if (drag.current?.moved) return;
              onChange(tab.id);
            }}
            className={`px-4 py-2 rounded-xl text-sm font-semibold transition whitespace-nowrap flex-shrink-0 ${
              active === tab.id ? 'bg-rose-500 text-white' : 'bg-white/5 text-gray-400 hover:text-white'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {canLeft && (
        <button
          type="button"
          aria-label="Cuộn tab sang trái"
          onClick={() => scrollBy(-ARROW_STEP_PX)}
          className="absolute left-0 top-0 h-[calc(100%-0.75rem)] w-10 flex items-center justify-start bg-gradient-to-r from-[#161824] via-[#161824]/90 to-transparent text-white"
        >
          <ChevronLeft className="w-5 h-5" />
        </button>
      )}
      {canRight && (
        <button
          type="button"
          aria-label="Cuộn tab sang phải"
          onClick={() => scrollBy(ARROW_STEP_PX)}
          className="absolute right-0 top-0 h-[calc(100%-0.75rem)] w-10 flex items-center justify-end bg-gradient-to-l from-[#161824] via-[#161824]/90 to-transparent text-white"
        >
          <ChevronRight className="w-5 h-5" />
        </button>
      )}
    </div>
  );
}
