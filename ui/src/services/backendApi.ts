import { ipc } from './ipc';

export interface HoleRectDto {
  x: number;
  y: number;
  w: number;
  h: number;
  rotation?: number;
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
}

const BACKEND_BASE_URL = (import.meta.env.VITE_BACKEND_API_URL || 'http://129.221.221.113:8080').replace(/\/+$/, '');

/**
 * Ánh xạ giữa kích cỡ giao diện (PhotoSizeType: strip2x6, postcard4x6, special4x6)
 * sang các LayoutId tương ứng trong cơ sở dữ liệu CMS (1x4, 2x2, 2x3, ...).
 */
export function mapSizeToLayoutIds(size?: string): string[] {
  if (!size) return [];
  const s = size.toLowerCase().trim();
  if (s === 'strip2x6' || s.includes('2x6') || s === '1x4' || s.includes('strip')) {
    return ['1x4', 'strip2x6', 'strips_2x6', '2x6'];
  }
  if (s === 'postcard4x6' || s === '2x2' || s.includes('grid')) {
    return ['2x2', 'postcard4x6', 'grid_4x6', '4x6'];
  }
  if (s === 'special4x6' || s === '2x3' || s.includes('postcard')) {
    return ['2x3', 'special4x6', 'postcard_4x6', '4x6'];
  }
  return [s];
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

  if (l === '2x3' || l.includes('6')) {
    // 6 ô bưu thiếp
    return [
      { x: 35, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 1110, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 1110, w: 550, h: 500, rotation: 0 },
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
    id: '3',
    name: 'Summer Pastel 6 Ô (2x3)',
    layoutId: '2x3',
    layoutName: 'Lưới 6 ảnh (2x3 Postcard)',
    categoryId: 1,
    categoryName: 'Hot Trend',
    frameUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145335_d98a8979cee64db5aa88733b8ef4f415.png',
    thumbnailUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145335_1654b14c10404898893ed260471f2268.png',
    holes: [
      { x: 35, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 1110, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 1110, w: 550, h: 500, rotation: 0 },
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
  {
    id: '5',
    name: 'Classic Grid 6 Ô (2x3)',
    layoutId: '2x3',
    layoutName: 'Lưới 6 ảnh (2x3 Postcard)',
    categoryId: 1,
    categoryName: 'Hot Trend',
    frameUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145337_4bf1448a22e64befb10790b6f65b0d4f.png',
    thumbnailUrl: 'https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145338_31b0f886dd204c4d81cce153676cb05d.png',
    holes: [
      { x: 35, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 50, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 580, w: 550, h: 500, rotation: 0 },
      { x: 35, y: 1110, w: 550, h: 500, rotation: 0 },
      { x: 615, y: 1110, w: 550, h: 500, rotation: 0 },
    ],
  },
];

export const backendApi = {
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
   * Tự động lọc theo layout tương ứng với kích cỡ (strip2x6, postcard4x6, ...)
   */
  async getFrames(layoutOrSize?: string): Promise<FrameOption[]> {
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
      // Lọc theo layout tương ứng nếu có
      const targetLayouts = mapSizeToLayoutIds(layoutOrSize);
      const filtered = targetLayouts.length > 0
        ? rawData.filter((f) => targetLayouts.includes((f.layoutId || '').toLowerCase()))
        : rawData;

      // Nếu có frame khớp layout thì lấy, nếu không lấy toàn bộ frame từ server (không dùng hardcode!)
      const targetList = filtered.length > 0 ? filtered : rawData;

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
        };
      });
    }

    // Nếu không kết nối được mạng, dùng FALLBACK_FRAMES (5 khung ảnh chính thức từ CMS)
    if (layoutOrSize && layoutOrSize !== 'all') {
      const targetLayouts = mapSizeToLayoutIds(layoutOrSize);
      const filteredFallback = targetLayouts.length > 0
        ? FALLBACK_FRAMES.filter((f) => targetLayouts.includes((f.layoutId || '').toLowerCase()))
        : FALLBACK_FRAMES;
      return filteredFallback.length > 0 ? filteredFallback : FALLBACK_FRAMES;
    }

    return FALLBACK_FRAMES;
  },
};
