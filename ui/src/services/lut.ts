import { ipc } from './ipc';

/**
 * LUT 3D (.cube) cho bộ lọc kiểu LUT của CMS. Cách đọc và áp giống hệt trang CMS (fe-cms/src/utils/filterConfig.ts)
 * và host (PhotoBooth.Imaging/Filters/PhotoFilterRenderer.cs) để xem trước khớp bản in.
 */
export interface CubeLut {
  size: number;
  /** size^3 * 3 giá trị RGB, chỉ số đỏ thay đổi nhanh nhất */
  data: Float32Array;
  domainMin: [number, number, number];
  domainMax: [number, number, number];
}

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

export const isCubeUrl = (url?: string | null) => !!url && /\.cube(\?.*)?$/i.test(url);

export function parseCubeLut(text: string): CubeLut {
  let size = 0;
  let domainMin: [number, number, number] = [0, 0, 0];
  let domainMax: [number, number, number] = [1, 1, 1];
  let data: Float32Array | null = null;
  let written = 0;

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const first = line.charCodeAt(0);
    const numeric = first === 45 || first === 46 || first === 43 || (first >= 48 && first <= 57);
    const parts = line.split(/\s+/);
    if (!numeric) {
      const key = parts[0].toUpperCase();
      if (key === 'LUT_1D_SIZE') throw new Error('Chỉ hỗ trợ LUT 3D');
      if (key === 'LUT_3D_SIZE') {
        size = parseInt(parts[1], 10);
        if (!Number.isInteger(size) || size < 2 || size > 129) throw new Error('LUT_3D_SIZE không hợp lệ');
        data = new Float32Array(size * size * size * 3);
      } else if (key === 'DOMAIN_MIN' || key === 'DOMAIN_MAX') {
        const triple = parts.slice(1, 4).map(Number) as [number, number, number];
        if (key === 'DOMAIN_MIN') domainMin = triple;
        else domainMax = triple;
      }
      continue;
    }
    if (!data) throw new Error('Thiếu LUT_3D_SIZE trước dữ liệu màu');
    if (parts.length < 3 || written + 3 > data.length) throw new Error('Dữ liệu LUT không khớp LUT_3D_SIZE');
    for (let c = 0; c < 3; c++) data[written++] = Number(parts[c]);
  }
  if (!data || written !== data.length) throw new Error('File .cube thiếu dữ liệu');
  return { size, data, domainMin, domainMax };
}

/** Áp LUT lên ImageData tại chỗ (nội suy trilinear), trộn với ảnh gốc theo intensity 0..1. */
export function applyCubeLut(image: ImageData, lut: CubeLut, intensity = 1): void {
  const { size, data, domainMin, domainMax } = lut;
  const px = image.data;
  const n1 = size - 1;
  const mix = clamp(intensity, 0, 1);
  if (mix === 0) return;
  const sr = n1 / (domainMax[0] - domainMin[0]);
  const sg = n1 / (domainMax[1] - domainMin[1]);
  const sb = n1 / (domainMax[2] - domainMin[2]);
  const stride = size * 3;
  const slice = size * size * 3;

  for (let i = 0; i < px.length; i += 4) {
    const r = clamp((px[i] / 255 - domainMin[0]) * sr, 0, n1);
    const g = clamp((px[i + 1] / 255 - domainMin[1]) * sg, 0, n1);
    const b = clamp((px[i + 2] / 255 - domainMin[2]) * sb, 0, n1);
    const r0 = Math.floor(r), g0 = Math.floor(g), b0 = Math.floor(b);
    const r1 = Math.min(r0 + 1, n1), g1 = Math.min(g0 + 1, n1), b1 = Math.min(b0 + 1, n1);
    const fr = r - r0, fg = g - g0, fb = b - b0;
    const i000 = r0 * 3 + g0 * stride + b0 * slice;
    const i100 = r1 * 3 + g0 * stride + b0 * slice;
    const i010 = r0 * 3 + g1 * stride + b0 * slice;
    const i110 = r1 * 3 + g1 * stride + b0 * slice;
    const i001 = r0 * 3 + g0 * stride + b1 * slice;
    const i101 = r1 * 3 + g0 * stride + b1 * slice;
    const i011 = r0 * 3 + g1 * stride + b1 * slice;
    const i111 = r1 * 3 + g1 * stride + b1 * slice;
    for (let c = 0; c < 3; c++) {
      const c00 = data[i000 + c] + (data[i100 + c] - data[i000 + c]) * fr;
      const c10 = data[i010 + c] + (data[i110 + c] - data[i010 + c]) * fr;
      const c01 = data[i001 + c] + (data[i101 + c] - data[i001 + c]) * fr;
      const c11 = data[i011 + c] + (data[i111 + c] - data[i011 + c]) * fr;
      const c0 = c00 + (c10 - c00) * fg;
      const c1 = c01 + (c11 - c01) * fg;
      const mapped = clamp(c0 + (c1 - c0) * fb, 0, 1) * 255;
      px[i + c] = px[i + c] + (mapped - px[i + c]) * mix;
    }
  }
}

const lutCache = new Map<string, Promise<CubeLut>>();

interface LutPayload {
  size?: number;
  domainMin?: number[];
  domainMax?: number[];
  /** base64 của size^3*3 số 16 bit không dấu little-endian (0..65535 = 0..1), cùng thứ tự với file .cube */
  data16?: string;
}

function lutFromPayload(res: LutPayload | null | undefined): CubeLut | null {
  const size = res?.size;
  if (!res || !size || typeof res.data16 !== 'string') return null;
  const bin = atob(res.data16);
  const count = size * size * size * 3;
  if (bin.length !== count * 2) throw new Error('Dữ liệu LUT không khớp kích thước');
  const data = new Float32Array(count);
  for (let i = 0; i < count; i++) data[i] = (bin.charCodeAt(i * 2) | (bin.charCodeAt(i * 2 + 1) << 8)) / 65535;
  const triple = (v: number[] | undefined, def: number): [number, number, number] =>
    Array.isArray(v) && v.length >= 3 ? [v[0], v[1], v[2]] : [def, def, def];
  return { size, data, domainMin: triple(res.domainMin, 0), domainMax: triple(res.domainMax, 1) };
}

/** Tải LUT: host trả bản đã đọc sẵn (nhị phân gọn, file lưu trong LutCache); dự phòng tải file .cube khi chạy trên trình duyệt. */
export function loadCubeLut(url: string): Promise<CubeLut> {
  let cached = lutCache.get(url);
  if (!cached) {
    cached = ipc.invokeStrict<LutPayload>('get_lut', { url }, 20000).then((res) => {
      const lut = lutFromPayload(res);
      if (lut) return lut;
      return fetch(url).then((r) => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.text().then(parseCubeLut);
      });
    });
    cached.catch(() => lutCache.delete(url));
    lutCache.set(url, cached);
  }
  return cached;
}

/** Cạnh dài tối đa của ảnh xem trước đã áp LUT. */
const MAX_PREVIEW_SIDE = 1280;

/** Bộ nhớ đệm có giới hạn: bỏ mục dùng lâu nhất khi đầy (ảnh của các lượt chụp trước). */
class LruCache<V> {
  private map = new Map<string, V>();
  private readonly max: number;
  private readonly onEvict: (v: V) => void;
  constructor(max: number, onEvict: (v: V) => void) {
    this.max = max;
    this.onEvict = onEvict;
  }
  get(key: string): V | undefined {
    const v = this.map.get(key);
    if (v !== undefined) {
      this.map.delete(key);
      this.map.set(key, v);
    }
    return v;
  }
  set(key: string, v: V) {
    this.map.delete(key);
    this.map.set(key, v);
    while (this.map.size > this.max) {
      const oldest = this.map.keys().next().value as string;
      const ov = this.map.get(oldest) as V;
      this.map.delete(oldest);
      this.onEvict(ov);
    }
  }
  delete(key: string) {
    this.map.delete(key);
  }
}

// Ảnh nguồn đã thu nhỏ: ảnh gốc vài chục MP chỉ giải mã một lần cho mọi bộ lọc
const sourceCache = new LruCache<Promise<ImageBitmap>>(24, (p) => {
  p.then((b) => b.close()).catch(() => undefined);
});

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('Không tải được ảnh'));
    img.src = src;
  });
}

function loadSource(src: string): Promise<ImageBitmap> {
  let cached = sourceCache.get(src);
  if (!cached) {
    cached = loadImage(src).then((img) => {
      const scale = Math.min(1, MAX_PREVIEW_SIDE / Math.max(img.naturalWidth, img.naturalHeight));
      return createImageBitmap(img, {
        resizeWidth: Math.max(1, Math.round(img.naturalWidth * scale)),
        resizeHeight: Math.max(1, Math.round(img.naturalHeight * scale)),
        resizeQuality: 'high',
      });
    });
    cached.catch(() => sourceCache.delete(src));
    sourceCache.set(src, cached);
  }
  return cached;
}

// ---------------------------------------------------------------- GPU (WebGL2): áp LUT trong vài mili giây

const VERT = `#version 300 es
in vec2 aPos;
out vec2 vUv;
void main() {
  vUv = aPos * 0.5 + 0.5;
  gl_Position = vec4(aPos, 0.0, 1.0);
}`;

// Cùng phép tính với applyCubeLut: chuẩn hoá theo DOMAIN, nội suy trilinear (phần cứng), trộn với ảnh gốc theo uMix
const FRAG = `#version 300 es
precision highp float;
precision highp sampler3D;
uniform sampler2D uImg;
uniform sampler3D uLut;
uniform vec3 uMin;
uniform vec3 uMax;
uniform float uSize;
uniform float uMix;
in vec2 vUv;
out vec4 outColor;
void main() {
  vec3 c = texture(uImg, vec2(vUv.x, 1.0 - vUv.y)).rgb;
  vec3 t = clamp((c - uMin) / (uMax - uMin), 0.0, 1.0);
  vec3 m = texture(uLut, t * ((uSize - 1.0) / uSize) + 0.5 / uSize).rgb;
  outColor = vec4(mix(c, clamp(m, 0.0, 1.0), uMix), 1.0);
}`;

type UniformName = 'uImg' | 'uLut' | 'uMin' | 'uMax' | 'uSize' | 'uMix';

interface GlState {
  gl: WebGL2RenderingContext;
  canvas: HTMLCanvasElement;
  uniforms: Record<UniformName, WebGLUniformLocation | null>;
  imgTex: WebGLTexture;
  luts: WeakMap<CubeLut, WebGLTexture>;
}

// undefined = chưa thử, null = không dùng được WebGL2 (áp bằng CPU)
let glState: GlState | null | undefined;

function compile(gl: WebGL2RenderingContext, type: number, src: string): WebGLShader {
  const sh = gl.createShader(type);
  if (!sh) throw new Error('createShader');
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh) || 'shader');
  return sh;
}

function getGl(): GlState | null {
  if (glState !== undefined) return glState;
  try {
    const canvas = document.createElement('canvas');
    const gl = canvas.getContext('webgl2', { preserveDrawingBuffer: true, antialias: false, alpha: false });
    if (!gl) throw new Error('Không có WebGL2');
    const prog = gl.createProgram();
    const imgTex = gl.createTexture();
    if (!prog || !imgTex) throw new Error('WebGL2 không tạo được tài nguyên');
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog) || 'link');
    gl.useProgram(prog);

    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
    const loc = gl.getAttribLocation(prog, 'aPos');
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);

    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, imgTex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    const u = (n: UniformName) => gl.getUniformLocation(prog, n);
    const uniforms: Record<UniformName, WebGLUniformLocation | null> = {
      uImg: u('uImg'),
      uLut: u('uLut'),
      uMin: u('uMin'),
      uMax: u('uMax'),
      uSize: u('uSize'),
      uMix: u('uMix'),
    };
    gl.uniform1i(uniforms.uImg, 0);
    gl.uniform1i(uniforms.uLut, 1);

    // Mất ngữ cảnh GPU (driver khởi động lại...): các lần sau áp bằng CPU
    canvas.addEventListener('webglcontextlost', () => {
      glState = null;
    });
    glState = { gl, canvas, uniforms, imgTex, luts: new WeakMap() };
  } catch (err) {
    console.warn('[lut] WebGL2 không dùng được, áp LUT bằng CPU:', err);
    glState = null;
  }
  return glState;
}

function lutTexture(st: GlState, lut: CubeLut): WebGLTexture {
  const existing = st.luts.get(lut);
  if (existing) return existing;
  const { gl } = st;
  const tex = gl.createTexture();
  if (!tex) throw new Error('createTexture');
  gl.activeTexture(gl.TEXTURE1);
  gl.bindTexture(gl.TEXTURE_3D, tex);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  // Chỉ số đỏ thay đổi nhanh nhất = trục x của texture 3D, rồi xanh lá (y), xanh dương (z)
  gl.texImage3D(gl.TEXTURE_3D, 0, gl.RGB16F, lut.size, lut.size, lut.size, 0, gl.RGB, gl.FLOAT, lut.data);
  gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_WRAP_R, gl.CLAMP_TO_EDGE);
  st.luts.set(lut, tex);
  return tex;
}

function canvasToUrl(canvas: HTMLCanvasElement): Promise<string> {
  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(URL.createObjectURL(blob)) : reject(new Error('Không tạo được ảnh'))),
      'image/jpeg',
      0.9,
    );
  });
}

/** Áp LUT bằng GPU; null nếu không dùng được WebGL2. */
function renderGpu(source: ImageBitmap, lut: CubeLut, intensity: number): Promise<string> | null {
  const st = getGl();
  if (!st) return null;
  const { gl, canvas, uniforms } = st;
  try {
    canvas.width = source.width;
    canvas.height = source.height;
    gl.viewport(0, 0, source.width, source.height);
    const tex = lutTexture(st, lut);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_3D, tex);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, st.imgTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, source);
    gl.uniform3f(uniforms.uMin, lut.domainMin[0], lut.domainMin[1], lut.domainMin[2]);
    gl.uniform3f(uniforms.uMax, lut.domainMax[0], lut.domainMax[1], lut.domainMax[2]);
    gl.uniform1f(uniforms.uSize, lut.size);
    gl.uniform1f(uniforms.uMix, clamp(intensity, 0, 1));
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    if (gl.getError() !== gl.NO_ERROR) throw new Error('Lỗi WebGL');
    // toBlob chụp ảnh của canvas ngay lúc gọi: lần vẽ sau (ảnh khác) không ghi đè
    return canvasToUrl(canvas);
  } catch (err) {
    console.warn('[lut] GPU lỗi, chuyển sang CPU:', err);
    glState = null;
    return null;
  }
}

/** Áp LUT bằng CPU (máy không có WebGL2). */
function renderCpu(source: ImageBitmap, lut: CubeLut, intensity: number): Promise<string> {
  const canvas = document.createElement('canvas');
  canvas.width = source.width;
  canvas.height = source.height;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) throw new Error('Canvas 2D không khả dụng');
  ctx.drawImage(source, 0, 0);
  const frame = ctx.getImageData(0, 0, canvas.width, canvas.height);
  applyCubeLut(frame, lut, intensity);
  ctx.putImageData(frame, 0, 0);
  return canvasToUrl(canvas);
}

// Ảnh đã áp LUT, theo (LUT, độ mạnh, ảnh nguồn): các bước và các ô dùng chung, không tính lại.
// Giới hạn số mục để ảnh của các lượt chụp cũ không chiếm bộ nhớ mãi.
const resultCache = new LruCache<Promise<string>>(160, (p) => {
  p.then((url) => URL.revokeObjectURL(url)).catch(() => undefined);
});

/** Ảnh (blob URL JPEG) đã áp LUT, cạnh dài tối đa 1280 px để xem trước nhanh. */
export function applyLutToImage(src: string, lutUrl: string, intensity: number): Promise<string> {
  const key = `${lutUrl}|${intensity}|${src}`;
  let cached = resultCache.get(key);
  if (!cached) {
    cached = Promise.all([loadSource(src), loadCubeLut(lutUrl)]).then(
      ([source, lut]) => renderGpu(source, lut, intensity) ?? renderCpu(source, lut, intensity),
    );
    cached.catch(() => resultCache.delete(key));
    resultCache.set(key, cached);
  }
  return cached;
}

/**
 * Nạp sẵn mọi LUT ngay lúc khởi động (chưa có ảnh): đọc dữ liệu từ host, dựng shader và đưa LUT lên GPU.
 * Tới bước chọn bộ lọc chỉ còn việc áp lên ảnh. Lần lượt từng LUT để không chiếm luồng giao diện lâu.
 */
export async function preloadLuts(filters: Array<{ lutUrl?: string }>): Promise<void> {
  const urls = [...new Set(filters.map((f) => f.lutUrl).filter((u): u is string => isCubeUrl(u)))];
  let ok = 0;
  for (const url of urls) {
    try {
      const lut = await loadCubeLut(url);
      const st = getGl();
      if (st) lutTexture(st, lut);
      ok++;
    } catch (err) {
      console.warn('[lut] Không nạp sẵn được LUT', url, err);
    }
    await new Promise((r) => setTimeout(r, 0));
  }
  console.log(`[lut] Đã nạp sẵn ${ok}/${urls.length} LUT`);
}

/**
 * Tính sẵn ảnh đã áp mọi bộ lọc LUT cho các ảnh (chạy nền, lần lượt từng ảnh để không giật giao diện):
 * khách chạm bộ lọc nào là có ngay. Gọi hàm trả về để dừng (rời màn hình).
 */
export function warmLutPreviews(photos: string[], filters: Array<{ lutUrl?: string; lutIntensity?: number }>): () => void {
  let stopped = false;
  const jobs = filters.filter((f) => isCubeUrl(f.lutUrl));
  void (async () => {
    for (const f of jobs) {
      for (const photo of photos) {
        if (stopped) return;
        try {
          await applyLutToImage(photo, f.lutUrl as string, f.lutIntensity ?? 1);
        } catch {
          // Lỗi một bộ lọc không chặn các bộ lọc khác; khi khách chọn sẽ thử lại
        }
        // Nhường luồng giao diện giữa các ảnh
        await new Promise((r) => setTimeout(r, 0));
      }
    }
  })();
  return () => {
    stopped = true;
  };
}
