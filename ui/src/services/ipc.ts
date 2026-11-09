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
            console.warn(`[IPC] Command '${command}' timeout after 4s. Using fallback mock.`);
            this.pendingRequests.delete(id);
            resolve(this.handleMockCommand<TResult>(command, payload));
          }
        }, 4000);

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
