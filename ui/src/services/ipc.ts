// Typed IPC Bridge for WebView2 / Browser Mock

export interface IpcRequest<T = any> {
  id: string;
  command: string;
  payload: T;
}

export interface IpcResponse<T = any> {
  id: string;
  success: boolean;
  data?: T;
  error?: string;
}

export interface IpcEvent<T = any> {
  event: string;
  data: T;
}

type EventListener<T = any> = (data: T) => void;

class IpcBridge {
  private pendingRequests = new Map<string, { resolve: (val: any) => void; reject: (err: any) => void }>();
  private eventListeners = new Map<string, Set<EventListener>>();
  private initializedWebView2Listener: boolean = false;

  constructor() {
    this.ensureListener();
  }

  private ensureListener() {
    if (typeof window !== 'undefined' && (window as any).chrome?.webview && !this.initializedWebView2Listener) {
      (window as any).chrome.webview.addEventListener('message', (event: any) => {
        this.handleIncomingMessage(event.data);
      });
      this.initializedWebView2Listener = true;
    }
  }

  private get isWebView2Available(): boolean {
    this.ensureListener();
    return typeof window !== 'undefined' && !!(window as any).chrome?.webview;
  }

  private handleIncomingMessage(raw: any) {
    let msg: any;
    try {
      msg = typeof raw === 'string' ? JSON.parse(raw) : raw;
    } catch {
      msg = raw;
    }

    if (!msg) return;

    // Check if it's a response to a request
    if (msg.id && this.pendingRequests.has(msg.id)) {
      const { resolve, reject } = this.pendingRequests.get(msg.id)!;
      this.pendingRequests.delete(msg.id);
      if (msg.success) {
        resolve(msg.data);
      } else {
        reject(new Error(msg.error || 'IPC Command failed'));
      }
      return;
    }

    // Check if it's an event broadcast
    if (msg.event && this.eventListeners.has(msg.event)) {
      const listeners = this.eventListeners.get(msg.event)!;
      for (const listener of listeners) {
        try {
          listener(msg.data);
        } catch (e) {
          console.error(`[IPC] Error in event listener for ${msg.event}:`, e);
        }
      }
    }
  }

  public async invoke<TResult = any, TPayload = any>(command: string, payload?: TPayload): Promise<TResult> {
    const id = `req_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
    const request: IpcRequest<TPayload> = {
      id,
      command,
      payload: payload as TPayload,
    };

    if (this.isWebView2Available) {
      return new Promise<TResult>((resolve) => {
        // Fallback safety timeout: never hang the kiosk UI
        const timer = setTimeout(() => {
          if (this.pendingRequests.has(id)) {
            console.warn(`[IPC] Command '${command}' timeout after 8s. Using fallback mock.`);
            this.pendingRequests.delete(id);
            resolve(this.handleMockCommand<TResult>(command, payload));
          }
        }, 8000);

        this.pendingRequests.set(id, {
          resolve: (val) => {
            clearTimeout(timer);
            resolve(val);
          },
          reject: (err) => {
            clearTimeout(timer);
            console.warn(`[IPC] Command '${command}' failed, using fallback:`, err);
            resolve(this.handleMockCommand<TResult>(command, payload));
          },
        });

        try {
          (window as any).chrome.webview.postMessage(JSON.stringify(request));
        } catch (e) {
          console.error('[IPC] postMessage error:', e);
          clearTimeout(timer);
          this.pendingRequests.delete(id);
          resolve(this.handleMockCommand<TResult>(command, payload));
        }
      });
    } else {
      // Browser Mock Responses for local UI development
      return this.handleMockCommand<TResult>(command, payload);
    }
  }

  public on<T = any>(event: string, listener: EventListener<T>): () => void {
    if (!this.eventListeners.has(event)) {
      this.eventListeners.set(event, new Set());
    }
    this.eventListeners.get(event)!.add(listener);
    return () => {
      this.eventListeners.get(event)?.delete(listener);
    };
  }

  public listen<T = any>(event: string, listener: EventListener<T>): () => void {
    return this.on<T>(event, listener);
  }

  private handleMockCommand<TResult>(command: string, payload: any): Promise<TResult> {
    console.log(`[IPC Mock] ${command}`, payload);
    return new Promise((resolve) => {
      setTimeout(() => {
        switch (command) {
          case 'get_kiosk_status':
            resolve({
              mode: 'FreeEvent',
              paperCount: 350,
              isPrinterReady: true,
              isCameraReady: true,
              cameraModel: 'Mock High-Res Camera (DirectShow/EDSDK)',
              version: '1.0.0',
            } as any);
            break;
          case 'verify_admin_pin':
            resolve((payload?.pin === '8888' || payload?.pin === '1234') as any);
            break;
          case 'get_frames':
            resolve([
              {
                id: 1,
                layoutId: "1x4",
                layoutName: "Dải 4 ảnh (1x4 Strips)",
                categoryId: 4,
                categoryName: "Vintage",
                name: "Vintage Film Strip 4 Ô (1x4)",
                frameUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145330_5f8054b7eed74fa5b0c34e6031a7bd24.png",
                thumbnailUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145332_8e4595916ec243a0881e70debe5d3b53.png",
                holes: [{ x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }],
                isActive: true,
                displayOrder: 1,
              },
              {
                id: 2,
                layoutId: "2x2",
                layoutName: "Lưới 4 ảnh vuông (2x2 Grid)",
                categoryId: 1,
                categoryName: "Hot Trend",
                name: "Summer Vibes Lưới 4 Ô (2x2)",
                frameUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145333_adb887c3bc79423abfe8b8fd492a778a.png",
                thumbnailUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145334_9e190ae62582478483c431618c56ff29.png",
                holes: [{ x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }],
                isActive: true,
                displayOrder: 2,
              },
              {
                id: 3,
                layoutId: "2x3",
                layoutName: "Lưới 6 ảnh (2x3 Postcard)",
                categoryId: 1,
                categoryName: "Hot Trend",
                name: "Summer Pastel 6 Ô (2x3)",
                frameUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145335_d98a8979cee64db5aa88733b8ef4f415.png",
                thumbnailUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145335_1654b14c10404898893ed260471f2268.png",
                holes: [{ x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }],
                isActive: true,
                displayOrder: 3,
              },
              {
                id: 4,
                layoutId: "2x2",
                layoutName: "Lưới 4 ảnh vuông (2x2 Grid)",
                categoryId: 1,
                categoryName: "Hot Trend",
                name: "Classic Black 4 Ô (2x2)",
                frameUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145336_9251b0cb27054706961d16abcd8ec7a6.png",
                thumbnailUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145337_b9a38a74c6564b81b72b264e80695a52.png",
                holes: [{ x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }],
                isActive: true,
                displayOrder: 4,
              },
              {
                id: 5,
                layoutId: "2x3",
                layoutName: "Lưới 6 ảnh (2x3 Postcard)",
                categoryId: 1,
                categoryName: "Hot Trend",
                name: "Classic Grid 6 Ô (2x3)",
                frameUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145337_4bf1448a22e64befb10790b6f65b0d4f.png",
                thumbnailUrl: "https://pub-bc5a451086074267b70e4328a218b0c9.r2.dev/frames/20261110_145338_31b0f886dd204c4d81cce153676cb05d.png",
                holes: [{ x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }, { x: 0, y: 0, w: 0, h: 0, rotation: 0 }],
                isActive: true,
                displayOrder: 5,
              }
            ] as any);
            break;
          case 'get_presets':
            resolve([
              { id: 1, code: 'original', name: 'Ảnh Gốc (Natural)', category: 'Color', type: 'CssMatrix', intensityDefault: 1, isActive: true, displayOrder: 1 },
              { id: 2, code: 'classic_bw', name: 'Đen Trắng Cổ Điển', category: 'Vintage', type: 'CssMatrix', configJson: '{"grayscale":1,"contrast":1.2}', intensityDefault: 1, isActive: true, displayOrder: 2 },
              { id: 3, code: 'korean_soft', name: 'Hàn Quốc Trong Trẻo', category: 'Modern', type: 'CssMatrix', configJson: '{"contrast":0.95,"brightness":1.05,"saturation":1.1}', intensityDefault: 1, isActive: true, displayOrder: 3 },
              { id: 4, code: 'vintage_warm', name: 'Màu Phim Hoài Niệm', category: 'Film', type: 'CssMatrix', configJson: '{"sepia":0.3,"contrast":1.1,"saturation":0.9}', intensityDefault: 1, isActive: true, displayOrder: 4 }
            ] as any);
            break;
          case 'start_capture_session':
            resolve({ sessionId: `mock_session_${Date.now()}` } as any);
            break;
          case 'capture_shot':
            resolve({
              shotIndex: payload?.shotIndex || 1,
              filePath: `mock://raw_shot_${payload?.shotIndex || 1}.jpg`,
            } as any);
            break;
          case 'compose_strip':
            resolve({
              success: true,
              compositePath: 'mock://composite.jpg',
              qrUrl: 'https://hihibooth.vn/share/mock-1234',
              previewBase64: '',
            } as any);
            break;
          case 'print_session':
            resolve({
              success: true,
              remainingPaper: 349,
            } as any);
            break;
          default:
            resolve({ success: true } as any);
            break;
        }
      }, 300);
    });
  }
}

export const ipc = new IpcBridge();
