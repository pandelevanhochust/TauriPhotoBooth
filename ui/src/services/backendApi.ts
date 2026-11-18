import { ipc } from './ipc';

export interface HoleRectDto {
  x: number;
  y: number;
  w: number;
  h: number;
  rotation?: number;
}

/**
 * Tỉ lệ rộng / cao của ô nhỏ nhất trong frame (như FavoriCurrent): phần ảnh chụp lọt vào ô khi in.
 * Trả về undefined nếu frame không có ô hợp lệ.
 */
export function smallestHoleAspect(holes?: HoleRectDto[]): number | undefined {
  const valid = (holes || []).filter((h) => h.w > 0 && h.h > 0);
  if (valid.length === 0) return undefined;
  const smallest = valid.reduce((a, b) => (b.w * b.h < a.w * a.h ? b : a));
  return smallest.w / smallest.h;
}

export interface PhotoFrameDto {
  id: number;
  layoutId: string;
  layoutName?: string;
  categoryId?: number;
  categoryName?: string;
  name: string;
  frameUrl: string;
  thumbnailUrl?: string;
  holes: HoleRectDto[];
  isActive: boolean;
  displayOrder: number;
  createdAt?: string;
  qrX?: number | null;
  qrY?: number | null;
  qrSize?: number | null;
}

export interface PhotoFilterDto {
  id: number;
  code: string;
  name: string;
  category: string;
  type: string;
  lutUrl?: string | null;
  thumbnailUrl?: string | null;
  intensityDefault: number;
  configJson?: string | null;
  isActive: boolean;
  displayOrder: number;
  createdAt?: string;
}

export interface FilterOption {
  id: string;
  code: string;
  name: string;
  cssFilter: string;
  thumbnailUrl?: string;
}

export interface FrameOption {
  id: string;
  name: string;
  frameUrl: string;
  thumbnailUrl: string;
  holes: HoleRectDto[];
  layoutId: string;
  layoutName?: string;
  categoryId?: number;
  categoryName?: string;
  bgColor?: string;
  borderColor?: string;
  /** Vị trí in QR tải ảnh (theo % frame, từ CMS): tâm qrX/qrY, cạnh qrSize theo chiều rộng. null = không in QR. */
  qrX?: number | null;
  qrY?: number | null;
  qrSize?: number | null;
}

export interface PricingTier {
  quantity: number;
  price: number;
}

/** Layout (kích cỡ ảnh) từ API GET /api/layouts: quyết định giá tiền và nhóm Frame hiển thị. */
export interface LayoutOption {
  id: string;
  name: string;
  slug: string;
  widthPx: number;
  heightPx: number;
  holeCount: number;
  pricingTiers: PricingTier[];
  displayOrder: number;
  isActive?: boolean;
}

const BACKEND_BASE_URL = (import.meta.env.VITE_BACKEND_API_URL || 'http://129.221.221.113:8080').replace(/\/+$/, '');

// Dùng khi không gọi được CMS lẫn host
const FALLBACK_LAYOUTS: LayoutOption[] = [
  {
    id: '1x4',
    name: 'Dài 4 ảnh 1 cột',
    slug: '1x4',
    widthPx: 1200,
    heightPx: 3600,
    holeCount: 4,
    pricingTiers: [{ quantity: 2, price: 80000 }],
    displayOrder: 1,
  },
  {
    id: '2x2',
    name: '2 hàng 4 ảnh 2 cột',
    slug: '2x2',
    widthPx: 2400,
    heightPx: 3600,
    holeCount: 4,
    pricingTiers: [{ quantity: 2, price: 120000 }],
    displayOrder: 2,
  },
];

/**
 * Tính tổng tiền cho `quantity` bản in theo bảng giá của layout.
 * Khớp đúng tier thì lấy giá tier; không khớp thì nhân theo đơn giá của tier gần nhất.
 */
export function priceForQuantity(layout: LayoutOption | undefined, quantity: number): number {
  const tiers = (layout?.pricingTiers || []).filter((t) => t.quantity > 0);
  if (tiers.length === 0) return 0;
  const exact = tiers.find((t) => t.quantity === quantity);
  if (exact) return exact.price;
  const nearest = tiers.reduce((best, t) =>
    Math.abs(t.quantity - quantity) < Math.abs(best.quantity - quantity) ? t : best,
  );
  return Math.round((nearest.price / nearest.quantity) * quantity);
}

/**
 * Đảm bảo tọa độ các ô ảnh (Holes) luôn hợp lệ cho việc hiển thị và ghép ảnh.
 * Nếu server trả về mảng 0 hoặc rỗng, tự động nội suy bố cục chuẩn theo layout.
 */
function ensureValidHoles(rawHoles: HoleRectDto[], layoutId: string): HoleRectDto[] {
  const hasValidHoles = Array.isArray(rawHoles) && rawHoles.length > 0 && rawHoles.some((h) => h.w > 0 && h.h > 0);
  if (hasValidHoles) {
    return rawHoles;
  }

  const l = (layoutId || '').toLowerCase();
  if (l === '1x4' || l.includes('strip') || l.includes('2x6')) {
    // 4 ô dải dọc
    return [
      { x: 30, y: 40, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 440, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 840, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 1240, w: 540, h: 370, rotation: 0 },
    ];
  }

  // Mặc định 4 ô lưới 2x2
  return [
    { x: 35, y: 50, w: 550, h: 750, rotation: 0 },
    { x: 615, y: 50, w: 550, h: 750, rotation: 0 },
    { x: 35, y: 850, w: 550, h: 750, rotation: 0 },
    { x: 615, y: 850, w: 550, h: 750, rotation: 0 },
  ];
}

/**
 * Chuyển đổi configJson từ Backend (ví dụ {"contrast": 1.2, "grayscale": 1.0})
 * thành chuỗi CSS filter chuẩn để áp dụng trực tiếp lên thẻ <img> và Canvas.
 */
export function convertFilterConfigToCss(configJson?: string | null): string {
  if (!configJson) return 'none';
  try {
    const parsed = typeof configJson === 'string' ? JSON.parse(configJson) : configJson;
    const filters: string[] = [];

    if (typeof parsed.grayscale === 'number') {
      filters.push(`grayscale(${parsed.grayscale})`);
    }
    if (typeof parsed.sepia === 'number') {
      filters.push(`sepia(${parsed.sepia})`);
    }
    if (typeof parsed.contrast === 'number') {
      filters.push(`contrast(${parsed.contrast})`);
    }
    if (typeof parsed.brightness === 'number') {
      const b = parsed.brightness > -1 && parsed.brightness < 1 ? 1 + parsed.brightness : parsed.brightness;
      filters.push(`brightness(${b})`);
    }
    if (typeof parsed.saturation === 'number') {
      filters.push(`saturate(${parsed.saturation})`);
    }
    if (typeof parsed.hueRotate === 'number') {
      filters.push(`hue-rotate(${parsed.hueRotate}deg)`);
    }

    return filters.length > 0 ? filters.join(' ') : 'none';
  } catch (err) {
    console.warn('Failed to parse filter configJson:', configJson, err);
    return 'none';
  }
}

// Fallback presets khi mất kết nối mạng
const FALLBACK_PRESETS: FilterOption[] = [
  { id: 'original', code: 'original', name: 'Ảnh Gốc (Natural)', cssFilter: 'none' },
  { id: 'classic_bw', code: 'classic_bw', name: 'Đen Trắng Cổ Điển', cssFilter: 'grayscale(1) contrast(1.2)' },
  { id: 'korean_soft', code: 'korean_soft', name: 'Hàn Quốc Trong Trẻo', cssFilter: 'contrast(0.95) brightness(1.05) saturate(1.1)' },
  { id: 'vintage_warm', code: 'vintage_warm', name: 'Màu Phim Hoài Niệm', cssFilter: 'sepia(0.3) contrast(1.1) saturate(0.9)' },
];

// Dữ liệu khung ảnh chính thức từ Backend CMS (http://129.221.221.113:8080/api/frames)
const FALLBACK_FRAMES: FrameOption[] = [
  {
    id: '1',
    name: 'Vintage Film Strip 4 Ô (1x4)',
    layoutId: '1x4',
    layoutName: 'Dải 4 ảnh (1x4 Strips)',
    categoryId: 4,
    categoryName: 'Vintage',
    frameUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145330_5f8054b7eed74fa5b0c34e6031a7bd24.png',
    thumbnailUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145332_8e4595916ec243a0881e70debe5d3b53.png',
    holes: [
      { x: 30, y: 40, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 440, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 840, w: 540, h: 370, rotation: 0 },
      { x: 30, y: 1240, w: 540, h: 370, rotation: 0 },
    ],
  },
  {
    id: '2',
    name: 'Summer Vibes Lưới 4 Ô (2x2)',
    layoutId: '2x2',
    layoutName: 'Lưới 4 ảnh vuông (2x2 Grid)',
    categoryId: 1,
    categoryName: 'Hot Trend',
    frameUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145333_adb887c3bc79423abfe8b8fd492a778a.png',
    thumbnailUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145334_9e190ae62582478483c431618c56ff29.png',
    holes: [
      { x: 35, y: 50, w: 550, h: 750, rotation: 0 },
      { x: 615, y: 50, w: 550, h: 750, rotation: 0 },
      { x: 35, y: 850, w: 550, h: 750, rotation: 0 },
      { x: 615, y: 850, w: 550, h: 750, rotation: 0 },
    ],
  },
  {
    id: '4',
    name: 'Classic Black 4 Ô (2x2)',
    layoutId: '2x2',
    layoutName: 'Lưới 4 ảnh vuông (2x2 Grid)',
    categoryId: 1,
    categoryName: 'Hot Trend',
    frameUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145336_9251b0cb27054706961d16abcd8ec7a6.png',
    thumbnailUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145337_b9a38a74c6564b81b72b264e80695a52.png',
    holes: [
      { x: 35, y: 50, w: 550, h: 750, rotation: 0 },
      { x: 615, y: 50, w: 550, h: 750, rotation: 0 },
      { x: 35, y: 850, w: 550, h: 750, rotation: 0 },
      { x: 615, y: 850, w: 550, h: 750, rotation: 0 },
    ],
  },
];

const sameLayout = (a: string | undefined, b: string | undefined) =>
  (a || '').toLowerCase().trim() === (b || '').toLowerCase().trim();

// Frame đã khoét ô theo frameUrl; mỗi frame chỉ xin host một lần
const frameOverlayCache = new Map<string, Promise<string | undefined>>();

export const backendApi = {
  /**
   * Frame PNG (data URL) đã được host khoét trong suốt các ô tô màu mẫu, để ảnh nằm dưới frame
   * mà vẫn lộ ra như bản in. Không có host (chạy trình duyệt) thì trả về undefined.
   */
  getFrameOverlay(frame: FrameOption): Promise<string | undefined> {
    const key = frame.frameUrl;
    if (!key) return Promise.resolve(undefined);
    let pending = frameOverlayCache.get(key);
    if (!pending) {
      pending = ipc
        .invoke<{ dataUrl?: string }>('get_frame_overlay', { frameUrl: key, holes: frame.holes })
        .then((res) => (typeof res?.dataUrl === 'string' && res.dataUrl.startsWith('data:image') ? res.dataUrl : undefined))
        .catch(() => undefined);
      pending.then((url) => {
        if (!url) frameOverlayCache.delete(key);
      });
      frameOverlayCache.set(key, pending);
    }
    return pending;
  },

  /**
   * Lấy danh sách layout (kích cỡ ảnh) từ API GET /api/layouts
   */
  async getLayouts(): Promise<LayoutOption[]> {
    let rawData: any[] | null = null;

    try {
      const ipcResult = await ipc.invoke<any[]>('get_layouts');
      if (Array.isArray(ipcResult) && ipcResult.length > 0) {
        rawData = ipcResult;
      }
    } catch (e) {
      console.warn('[backendApi] IPC get_layouts failed or not available:', e);
    }

    if (!rawData) {
      try {
        const res = await fetch(`${BACKEND_BASE_URL}/api/layouts?onlyActive=false`, {
          headers: { Accept: 'application/json' },
        });
        if (res.ok) {
          const fetched = await res.json();
          if (Array.isArray(fetched) && fetched.length > 0) {
            rawData = fetched;
          }
        }
      } catch (err) {
        console.warn('[backendApi] Fetch layouts failed:', err);
      }
    }

    if (!rawData) return FALLBACK_LAYOUTS;

    const layouts = rawData
      .filter((l) => l && l.id && l.isActive !== false)
      .map<LayoutOption>((l) => ({
        id: String(l.id),
        name: l.name || String(l.id),
        slug: l.slug || String(l.id),
        widthPx: Number(l.widthPx) || 2400,
        heightPx: Number(l.heightPx) || 3600,
        holeCount: Number(l.holeCount) || 4,
        pricingTiers: Array.isArray(l.pricingTiers)
          ? l.pricingTiers.map((t: any) => ({ quantity: Number(t.quantity) || 0, price: Number(t.price) || 0 }))
          : [],
        displayOrder: Number(l.displayOrder) || 0,
      }))
      .sort((a, b) => a.displayOrder - b.displayOrder);

    return layouts.length > 0 ? layouts : FALLBACK_LAYOUTS;
  },

  /**
   * Lấy danh sách bộ lọc ảnh từ API GET /api/presets
   */
  async getPresets(): Promise<FilterOption[]> {
    let rawData: PhotoFilterDto[] | null = null;

    // 1. Ưu tiên gọi qua C# Host IPC (bỏ qua mọi giới hạn Mixed Content/SSL)
    try {
      const ipcResult = await ipc.invoke<PhotoFilterDto[]>('get_presets');
      if (Array.isArray(ipcResult) && ipcResult.length > 0) {
        console.log('[backendApi] Received presets via IPC:', ipcResult.length);
        rawData = ipcResult;
      }
    } catch (e) {
      console.warn('[backendApi] IPC get_presets failed or not available:', e);
    }

    // 2. Dự phòng bằng fetch trực tiếp
    if (!rawData) {
      try {
        const url = `${BACKEND_BASE_URL}/api/presets?onlyActive=true`;
        console.log('[backendApi] Fetching presets via HTTP:', url);
        const res = await fetch(url, { headers: { Accept: 'application/json' } });
        if (res.ok) {
          const fetched = await res.json();
          if (Array.isArray(fetched) && fetched.length > 0) {
            rawData = fetched;
          }
        }
      } catch (err) {
        console.warn('[backendApi] Fetch presets failed:', err);
      }
    }

    if (rawData && rawData.length > 0) {
      return rawData.map((item) => ({
        id: item.code || String(item.id),
        code: item.code,
        name: item.name,
        cssFilter: convertFilterConfigToCss(item.configJson),
        thumbnailUrl: item.thumbnailUrl || undefined,
      }));
    }

    return FALLBACK_PRESETS;
  },

  /**
   * Lấy danh sách khung ảnh từ API GET /api/frames
   * Có `layoutId` thì chỉ trả về các frame thuộc đúng layout đó (có thể rỗng).
   */
  async getFrames(layoutId?: string): Promise<FrameOption[]> {
    let rawData: PhotoFrameDto[] | null = null;

    // 1. Ưu tiên gọi qua C# Host IPC (bỏ qua mọi giới hạn Mixed Content/SSL của WebView2)
    try {
      const ipcResult = await ipc.invoke<PhotoFrameDto[]>('get_frames');
      if (Array.isArray(ipcResult) && ipcResult.length > 0) {
        console.log('[backendApi] Received frames via IPC:', ipcResult.length);
        rawData = ipcResult;
      }
    } catch (e) {
      console.warn('[backendApi] IPC get_frames failed or not available:', e);
    }

    // 2. Dự phòng bằng fetch trực tiếp
    if (!rawData) {
      try {
        const url = `${BACKEND_BASE_URL}/api/frames?onlyActive=true`;
        console.log('[backendApi] Fetching frames via HTTP:', url);
        const res = await fetch(url, { headers: { Accept: 'application/json' } });
        if (res.ok) {
          const fetched = await res.json();
          if (Array.isArray(fetched) && fetched.length > 0) {
            rawData = fetched;
          }
        }
      } catch (err) {
        console.warn('[backendApi] Fetch frames failed:', err);
      }
    }

    if (rawData && rawData.length > 0) {
      const targetList = layoutId ? rawData.filter((f) => sameLayout(f.layoutId, layoutId)) : rawData;

      return targetList.map((f) => {
        const rawHoles: HoleRectDto[] = (f.holes || []).map((h: any) => ({
          x: Number(h.x ?? h.X ?? 0),
          y: Number(h.y ?? h.Y ?? 0),
          w: Number(h.w ?? h.W ?? 0),
          h: Number(h.h ?? h.H ?? 0),
          rotation: Number(h.rotation ?? h.Rotation ?? 0),
        }));

        const holes = ensureValidHoles(rawHoles, f.layoutId);
        const frameUrl = f.frameUrl.startsWith('http')
          ? f.frameUrl
          : `${BACKEND_BASE_URL}${f.frameUrl.startsWith('/') ? '' : '/'}${f.frameUrl}`;
        const thumbnailUrl = f.thumbnailUrl
          ? f.thumbnailUrl.startsWith('http')
            ? f.thumbnailUrl
            : `${BACKEND_BASE_URL}${f.thumbnailUrl.startsWith('/') ? '' : '/'}${f.thumbnailUrl}`
          : frameUrl;

        return {
          id: String(f.id),
          name: f.name,
          frameUrl,
          thumbnailUrl,
          holes,
          layoutId: f.layoutId,
          layoutName: f.layoutName,
          categoryId: f.categoryId,
          categoryName: f.categoryName,
          qrX: typeof f.qrX === 'number' ? f.qrX : null,
          qrY: typeof f.qrY === 'number' ? f.qrY : null,
          qrSize: typeof f.qrSize === 'number' ? f.qrSize : null,
        };
      });
    }

    // Không kết nối được mạng: dùng FALLBACK_FRAMES
    return layoutId ? FALLBACK_FRAMES.filter((f) => sameLayout(f.layoutId, layoutId)) : FALLBACK_FRAMES;
  },
};
