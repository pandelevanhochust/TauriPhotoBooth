import { FaceLandmarker, FilesetResolver } from '@mediapipe/tasks-vision';
import { LruCache, loadSource } from './lut';

/**
 * Làm mịn da (thử nghiệm): MediaPipe Face Landmarker tìm khuôn mặt, dựng mặt nạ vùng da (bỏ mắt, lông mày, môi),
 * rồi một bộ lọc bilateral trên GPU làm mịn da trong mặt nạ nhưng giữ cạnh (mi mắt, viền môi, tóc).
 * Không đổi hình dạng khuôn mặt, không tìm thấy mặt thì giữ nguyên ảnh.
 *
 * Mô hình và WASM nằm cạnh giao diện (public/mediapipe) nên chạy được khi không có mạng.
 */
export type SkinLevel = 'off' | 'light' | 'medium';

interface LevelParams {
  /** Mức trộn giữa ảnh gốc và ảnh đã làm mịn trong vùng da (0..1). */
  strength: number;
  /** Bán kính lọc, tính theo bề rộng khuôn mặt. */
  radiusFace: number;
  /** Độ nhạy theo màu: càng nhỏ càng giữ cạnh; càng lớn càng mịn. */
  sigmaR: number;
  /** Phần chi tiết (kết cấu da) giữ lại sau khi làm mịn (0..1), tránh da "nhựa". */
  detail: number;
  /** Làm sáng da nhẹ (0..1). */
  bright: number;
}

const PARAMS: Record<Exclude<SkinLevel, 'off'>, LevelParams> = {
  light: { strength: 0.6, radiusFace: 0.012, sigmaR: 0.08, detail: 0.3, bright: 0.02 },
  medium: { strength: 0.85, radiusFace: 0.02, sigmaR: 0.12, detail: 0.2, bright: 0.04 },
};

export const SKIN_LEVEL_LABELS: Record<SkinLevel, string> = { off: 'Tắt', light: 'Nhẹ', medium: 'Vừa' };

// ---------------------------------------------------------------- Nhận diện khuôn mặt

interface Pt {
  x: number;
  y: number;
}

// Chỉ số điểm trên lưới mặt 468 điểm của MediaPipe (FACEMESH_FACE_OVAL, EYES, EYEBROWS, LIPS)
const FACE_OVAL = [
  10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136,
  172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109,
];
const EXCLUDED_REGIONS: number[][] = [
  [33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246], // mắt phải
  [263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466], // mắt trái
  [46, 53, 52, 65, 55, 107, 66, 105, 63, 70], // lông mày phải
  [276, 283, 282, 295, 285, 336, 296, 334, 293, 300], // lông mày trái
  [61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185], // môi
];
const CHEEK_LEFT = 234;
const CHEEK_RIGHT = 454;
const MAX_FACES = 6;

let landmarkerPromise: Promise<FaceLandmarker> | null = null;

function getLandmarker(): Promise<FaceLandmarker> {
  if (!landmarkerPromise) {
    landmarkerPromise = (async () => {
      const base = new URL('mediapipe/', document.baseURI).href;
      const fileset = await FilesetResolver.forVisionTasks(base + 'wasm');
      // CPU: ổn định trên mọi máy kiosk (WebView2 không phải lúc nào cũng cho GPU delegate); ảnh 1280 px chỉ vài trăm ms
      return FaceLandmarker.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: base + 'face_landmarker.task', delegate: 'CPU' },
        runningMode: 'IMAGE',
        numFaces: MAX_FACES,
      });
    })();
    landmarkerPromise.catch((err) => {
      console.warn('[skin] Không nạp được Face Landmarker:', err);
      landmarkerPromise = null;
    });
  }
  return landmarkerPromise;
}

/** Nạp sẵn mô hình lúc khởi động để lần làm mịn đầu tiên không phải chờ. */
export async function preloadSkin(): Promise<void> {
  try {
    await getLandmarker();
  } catch {
    // đã ghi log; khi cần dùng sẽ thử lại
  }
}

const facesCache = new LruCache<Promise<Pt[][]>>(48, () => undefined);

function detectFaces(src: string): Promise<Pt[][]> {
  let cached = facesCache.get(src);
  if (!cached) {
    cached = Promise.all([loadSource(src), getLandmarker()]).then(([bitmap, landmarker]) => {
      const result = landmarker.detect(bitmap);
      return result.faceLandmarks.map((face) => face.slice(0, 468).map((p) => ({ x: p.x, y: p.y })));
    });
    cached.catch(() => facesCache.delete(src));
    facesCache.set(src, cached);
  }
  return cached;
}

// ---------------------------------------------------------------- Mặt nạ vùng da

function buildMask(w: number, h: number, faces: Pt[][], featherPx: number): HTMLCanvasElement {
  const raw = document.createElement('canvas');
  raw.width = w;
  raw.height = h;
  const ctx = raw.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D không khả dụng');
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, w, h);
  ctx.lineJoin = 'round';

  for (const face of faces) {
    const trace = (indices: number[]) => {
      ctx.beginPath();
      indices.forEach((i, k) => {
        const p = face[i];
        if (k === 0) ctx.moveTo(p.x * w, p.y * h);
        else ctx.lineTo(p.x * w, p.y * h);
      });
      ctx.closePath();
    };
    ctx.fillStyle = '#fff';
    trace(FACE_OVAL);
    ctx.fill();
    // Mắt, lông mày, môi: tô đen và nới ra một chút để không làm mờ lông mi, viền môi
    ctx.fillStyle = '#000';
    ctx.strokeStyle = '#000';
    ctx.lineWidth = featherPx * 1.5;
    for (const region of EXCLUDED_REGIONS) {
      trace(region);
      ctx.fill();
      ctx.stroke();
    }
  }

  // Làm mềm biên để vùng làm mịn không lộ ranh giới
  const feathered = document.createElement('canvas');
  feathered.width = w;
  feathered.height = h;
  const fctx = feathered.getContext('2d');
  if (!fctx) throw new Error('Canvas 2D không khả dụng');
  fctx.filter = `blur(${featherPx}px)`;
  fctx.drawImage(raw, 0, 0);
  return feathered;
}

// ---------------------------------------------------------------- Bộ lọc bilateral trên GPU (WebGL2)

const VERT = `#version 300 es
in vec2 aPos;
out vec2 vUv;
void main() {
  vUv = aPos * 0.5 + 0.5;
  gl_Position = vec4(aPos, 0.0, 1.0);
}`;

const FRAG = `#version 300 es
precision highp float;
uniform sampler2D uImg;
uniform sampler2D uMask;
uniform vec2 uTexel;
uniform float uRadius;
uniform float uSigmaR;
uniform float uStrength;
uniform float uDetail;
uniform float uBright;
in vec2 vUv;
out vec4 outColor;
const int N = 4;
void main() {
  vec2 uv = vec2(vUv.x, 1.0 - vUv.y);
  vec3 c = texture(uImg, uv).rgb;
  float m = texture(uMask, uv).r;
  if (m < 0.004) {
    outColor = vec4(c, 1.0);
    return;
  }
  float stepPx = uRadius / float(N);
  vec3 acc = vec3(0.0);
  float wsum = 0.0;
  for (int j = -N; j <= N; j++) {
    for (int i = -N; i <= N; i++) {
      vec2 d = vec2(float(i), float(j)) * stepPx;
      vec3 s = texture(uImg, uv + d * uTexel).rgb;
      vec3 diff = s - c;
      float ws = dot(d, d) / (uRadius * uRadius);
      float wr = dot(diff, diff) / (uSigmaR * uSigmaR);
      float w = exp(-1.5 * ws - wr);
      acc += s * w;
      wsum += w;
    }
  }
  vec3 smoothed = acc / wsum;
  // Giữ lại một phần chi tiết gốc để da còn kết cấu, rồi làm sáng nhẹ
  vec3 res = mix(smoothed, c, uDetail);
  res = pow(res, vec3(1.0 - uBright));
  outColor = vec4(mix(c, res, m * uStrength), 1.0);
}`;

interface GlState {
  gl: WebGL2RenderingContext;
  canvas: HTMLCanvasElement;
  prog: WebGLProgram;
}

// undefined = chưa thử, null = không có WebGL2
let glState: GlState | null | undefined;

function compile(gl: WebGL2RenderingContext, type: number, source: string): WebGLShader {
  const sh = gl.createShader(type);
  if (!sh) throw new Error('createShader');
  gl.shaderSource(sh, source);
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
    if (!prog) throw new Error('createProgram');
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
    canvas.addEventListener('webglcontextlost', () => {
      glState = undefined; // dựng lại ở lần sau
    });
    glState = { gl, canvas, prog };
  } catch (err) {
    console.warn('[skin] WebGL2 không dùng được:', err);
    glState = null;
  }
  return glState;
}

function makeTexture(gl: WebGL2RenderingContext, unit: number, source: TexImageSource): WebGLTexture {
  const tex = gl.createTexture();
  if (!tex) throw new Error('createTexture');
  gl.activeTexture(gl.TEXTURE0 + unit);
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, source);
  return tex;
}

function renderSmooth(source: ImageBitmap, faces: Pt[][], params: LevelParams): Promise<string> {
  const st = getGl();
  if (!st) throw new Error('Không có WebGL2');
  const { gl, canvas, prog } = st;
  const w = source.width;
  const h = source.height;

  // Bề rộng khuôn mặt trung bình (px) quyết định bán kính lọc và độ mềm biên mặt nạ
  const faceWidths = faces.map((f) => Math.hypot((f[CHEEK_RIGHT].x - f[CHEEK_LEFT].x) * w, (f[CHEEK_RIGHT].y - f[CHEEK_LEFT].y) * h));
  const faceWidth = faceWidths.reduce((a, b) => a + b, 0) / faceWidths.length;
  const radius = Math.min(10, Math.max(1.5, faceWidth * params.radiusFace));
  const feather = Math.max(2, faceWidth * 0.03);
  const mask = buildMask(w, h, faces, feather);

  canvas.width = w;
  canvas.height = h;
  gl.viewport(0, 0, w, h);
  gl.useProgram(prog);
  const imgTex = makeTexture(gl, 0, source);
  const maskTex = makeTexture(gl, 1, mask);
  const u = (name: string) => gl.getUniformLocation(prog, name);
  gl.uniform1i(u('uImg'), 0);
  gl.uniform1i(u('uMask'), 1);
  gl.uniform2f(u('uTexel'), 1 / w, 1 / h);
  gl.uniform1f(u('uRadius'), radius);
  gl.uniform1f(u('uSigmaR'), params.sigmaR);
  gl.uniform1f(u('uStrength'), params.strength);
  gl.uniform1f(u('uDetail'), params.detail);
  gl.uniform1f(u('uBright'), params.bright);
  gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  const error = gl.getError();
  gl.deleteTexture(imgTex);
  gl.deleteTexture(maskTex);
  if (error !== gl.NO_ERROR) throw new Error(`Lỗi WebGL ${error}`);

  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(URL.createObjectURL(blob)) : reject(new Error('Không tạo được ảnh'))),
      'image/jpeg',
      0.92,
    );
  });
}

// ---------------------------------------------------------------- API

const resultCache = new LruCache<Promise<string>>(64, (p) => {
  p.then((url) => {
    if (url.startsWith('blob:')) URL.revokeObjectURL(url);
  }).catch(() => undefined);
});

/**
 * Ảnh (blob URL) đã làm mịn da; cạnh dài tối đa 1280 px như các ảnh xem trước khác.
 * Tắt, không thấy mặt hoặc có lỗi thì trả lại đúng ảnh gốc.
 */
export function smoothSkinImage(src: string, level: SkinLevel): Promise<string> {
  if (level === 'off') return Promise.resolve(src);
  const key = `${level}|${src}`;
  let cached = resultCache.get(key);
  if (!cached) {
    cached = (async () => {
      const t0 = performance.now();
      const [source, faces] = await Promise.all([loadSource(src), detectFaces(src)]);
      if (faces.length === 0) {
        console.log('[skin] Không thấy khuôn mặt, giữ ảnh gốc');
        return src;
      }
      const out = await renderSmooth(source, faces, PARAMS[level]);
      console.log(`[skin] ${faces.length} mặt, ${Math.round(performance.now() - t0)} ms`);
      return out;
    })().catch((err) => {
      // Không nhớ kết quả lỗi: lần sau thử lại
      console.warn('[skin] Làm mịn lỗi, giữ ảnh gốc:', err);
      resultCache.delete(key);
      return src;
    });
    resultCache.set(key, cached);
  }
  return cached;
}
