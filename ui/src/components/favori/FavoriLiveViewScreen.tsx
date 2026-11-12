import React, { useState, useEffect, useRef } from 'react';
import { FlipHorizontal, Camera } from 'lucide-react';
import { ipc } from '../../services/ipc';

interface FavoriLiveViewScreenProps {
  totalShots?: number;
  mode?: 'remote' | 'autoCountdown';
  onAllShotsCaptured: (photos: string[]) => void;
  onCancel: () => void;
}

export const FavoriLiveViewScreen: React.FC<FavoriLiveViewScreenProps> = ({
  totalShots = 4,
  mode = 'autoCountdown',
  onAllShotsCaptured,
}) => {
  const [currentShot, setCurrentShot] = useState<number>(1);
  const [countdown, setCountdown] = useState<number | null>(mode === 'autoCountdown' ? 3 : null);
  const [isMirrored, setIsMirrored] = useState<boolean>(true);
  const [capturedPhotos, setCapturedPhotos] = useState<string[]>([]);
  const [isFlashing, setIsFlashing] = useState<boolean>(false);
  const [cameraReady, setCameraReady] = useState<boolean>(false);
  const [liveFrameBase64, setLiveFrameBase64] = useState<string | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);

  // Listen to live camera frames from C# host (DSLR / Canon / MockCamera)
  useEffect(() => {
    const unlisten = ipc.listen<{ base64: string }>('camera_frame', (data: { base64: string }) => {
      if (data && data.base64) {
        setLiveFrameBase64(`data:image/jpeg;base64,${data.base64}`);
        setCameraReady(true);
      }
    });
    return () => {
      unlisten();
    };
  }, []);

  // Setup WebCam or MediaStream
  useEffect(() => {
    let stream: MediaStream | null = null;
    let isCancelled = false;

    async function initWebcam() {
      try {
        console.log('[FavoriLiveViewScreen] Requesting webcam media stream...');
        let s: MediaStream;
        try {
          s = await navigator.mediaDevices.getUserMedia({
            video: {
              width: { ideal: 1920, min: 640 },
              height: { ideal: 1080, min: 480 },
              facingMode: 'user',
            },
            audio: false,
          });
        } catch (strictErr) {
          console.warn('[FavoriLiveViewScreen] Strict constraints failed, falling back to basic video: true', strictErr);
          s = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        }

        if (isCancelled) {
          s.getTracks().forEach((t) => t.stop());
          return;
        }

        stream = s;
        if (videoRef.current) {
          videoRef.current.srcObject = s;
          try {
            await videoRef.current.play();
          } catch (playErr) {
            console.warn('[FavoriLiveViewScreen] Video play auto-play warning:', playErr);
          }
          setCameraReady(true);
          console.log('[FavoriLiveViewScreen] Webcam connected and streaming successfully!');
        }
      } catch (err) {
        console.error('[FavoriLiveViewScreen] Webcam stream not accessible:', err);
        setCameraReady(true);
      }
    }

    initWebcam();

    return () => {
      isCancelled = true;
      if (stream) {
        stream.getTracks().forEach((track) => track.stop());
      }
    };
  }, []);

  // Shutter Flash sound & animation trigger
  const triggerCapture = async () => {
    setIsFlashing(true);
    setTimeout(() => setIsFlashing(false), 250);

    // Audio click sound using Web Audio API
    try {
      const audioCtx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)();
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();
      osc.connect(gain);
      gain.connect(audioCtx.destination);
      osc.frequency.setValueAtTime(800, audioCtx.currentTime);
      gain.gain.setValueAtTime(0.3, audioCtx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.1);
      osc.start();
      osc.stop(audioCtx.currentTime + 0.1);
      setTimeout(() => {
        audioCtx.close().catch(() => {});
      }, 200);
    } catch {
      // Audio context might be restricted
    }

    let photoData = '';
    // Capture from video element if active
    if (videoRef.current && videoRef.current.videoWidth > 0) {
      const canvas = document.createElement('canvas');
      canvas.width = videoRef.current.videoWidth;
      canvas.height = videoRef.current.videoHeight;
      const ctx = canvas.getContext('2d');
      if (ctx) {
        if (isMirrored) {
          ctx.translate(canvas.width, 0);
          ctx.scale(-1, 1);
        }
        ctx.drawImage(videoRef.current, 0, 0);
        photoData = canvas.toDataURL('image/jpeg', 0.95);
      }
    }

    // Also call host IPC capture and pass real webcam photo
    try {
      const result = await ipc.invoke<{ filePath?: string; base64?: string }>('capture_shot', {
        shotIndex: currentShot,
        base64: photoData,
      });
      if (result && result.filePath && !photoData) {
        photoData = result.filePath;
      }
    } catch (e) {
      console.warn('Host capture call error (using browser capture):', e);
    }

    if (!photoData && liveFrameBase64) {
      photoData = liveFrameBase64;
    }

    if (!photoData) {
      // Offline fallback canvas data URL (không phụ thuộc internet bên ngoài)
      const c = document.createElement('canvas');
      c.width = 1200;
      c.height = 800;
      const ctx = c.getContext('2d');
      if (ctx) {
        ctx.fillStyle = currentShot % 2 === 0 ? '#f5ece2' : '#e8dcce';
        ctx.fillRect(0, 0, 1200, 800);
        ctx.fillStyle = '#7a0000';
        ctx.font = 'bold 44px serif';
        ctx.textAlign = 'center';
        ctx.fillText(`Favori Photo Shot #${currentShot}`, 600, 400);
        photoData = c.toDataURL('image/jpeg', 0.9);
      }
    }

    const updated = [...capturedPhotos, photoData];
    setCapturedPhotos(updated);

    if (currentShot < totalShots) {
      setCurrentShot((s) => s + 1);
      if (mode === 'autoCountdown') {
        setCountdown(3);
      } else {
        setCountdown(null);
      }
    } else {
      // All shots completed
      setTimeout(() => {
        onAllShotsCaptured(updated);
      }, 500);
    }
  };

  // Countdown timer loop
  useEffect(() => {
    if (countdown === null) return;
    if (countdown > 0) {
      const timer = setTimeout(() => {
        setCountdown((c) => (c !== null ? c - 1 : null));
      }, 1000);
      return () => clearTimeout(timer);
    } else if (countdown === 0) {
      triggerCapture();
    }
  }, [countdown]);

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#f5f0ea] flex flex-col justify-between p-6 select-none">
      {/* Background with floral corners */}
      <div 
        className="absolute inset-0 pointer-events-none bg-cover bg-center z-0 opacity-90"
        style={{ backgroundImage: `url('/favori/Background.png')` }}
      />

      {/* Screen Shutter Flash */}
      {isFlashing && (
        <div className="absolute inset-0 bg-white z-50 pointer-events-none animate-shutter-flash" />
      )}

      {/* Top Header matching 8.png */}
      <header className="relative z-10 w-full flex items-center justify-between px-6 pt-2">
        {/* Top Left Logo with BƯỚC 7/11 directly below */}
        <div className="flex flex-col items-start">
          <img 
            src="/favori/logo.png" 
            alt="Favori" 
            className="h-14 object-contain filter drop-shadow-sm" 
          />
          <span className="text-[#7a0000] text-sm font-bold tracking-wider uppercase mt-0.5">
            BƯỚC 7/11
          </span>
        </div>

        {/* Top Center Title */}
        <div className="text-center">
          <h2 className="text-3xl md:text-4xl font-lobster text-[#1f1610]">
            Nhìn vào khung hình nhé
          </h2>
        </div>

        {/* Top Right Balancing Placeholder */}
        <div className="w-28" />
      </header>

      {/* Viewfinder Camera Feed Section - Expanded for large Kiosk display */}
      <main className="relative z-10 flex-1 flex flex-col items-center justify-center my-auto w-full max-w-6xl mx-auto px-2 md:px-4">
        <div className="relative w-full max-w-5xl h-[68vh] md:h-[72vh] max-h-[820px] bg-neutral-950 rounded-3xl overflow-hidden shadow-2xl border-4 border-[#bfa282]/90 flex items-center justify-center">
          {/* Live Video Feed (Dual-Source: Browser Webcam prioritized or C# Camera Stream) */}
          <video
            ref={videoRef}
            autoPlay
            playsInline
            muted
            className={`w-full h-full object-cover transition-transform ${
              isMirrored ? 'scale-x-[-1]' : 'scale-x-1'
            } ${liveFrameBase64 ? 'hidden' : 'block'}`}
          />
          {liveFrameBase64 && (
            <img
              src={liveFrameBase64}
              alt="Live View"
              className={`w-full h-full object-cover transition-transform ${
                isMirrored ? 'scale-x-[-1]' : 'scale-x-1'
              }`}
            />
          )}

          {!cameraReady && (
            <div className="absolute inset-0 flex items-center justify-center bg-neutral-900 text-white font-serif-vintage italic text-xl">
              Đang kết nối camera ...
            </div>
          )}

          {/* Camera Status Badge */}
          {cameraReady && (
            <div className="absolute bottom-4 left-4 bg-black/60 backdrop-blur-md px-3 py-1 rounded-full text-white/90 text-xs flex items-center gap-1.5 border border-white/10">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
              HD Webcam Live
            </div>
          )}

          {/* Shot Count Badge (Top-left inside viewfinder) */}
          <div className="absolute top-4 left-4 bg-black/60 backdrop-blur-md px-4 py-1.5 rounded-full text-white font-bold text-sm tracking-wider border border-white/20">
            Ảnh {currentShot} / {totalShots}
          </div>

          {/* Mirror Flip Toggle Button (Top-right inside viewfinder) */}
          <button
            onClick={() => setIsMirrored(!isMirrored)}
            className="absolute top-4 right-4 bg-black/60 hover:bg-black/80 backdrop-blur-md p-2.5 rounded-full text-white transition-all border border-white/20 active:scale-95 cursor-pointer"
            title="Lật gương"
          >
            <FlipHorizontal className="w-5 h-5" />
          </button>

          {/* Prominent Center Countdown Overlay */}
          {countdown !== null && countdown > 0 && (
            <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
              <span className="text-white text-8xl md:text-9xl font-bold font-serif-vintage italic drop-shadow-[0_10px_20px_rgba(0,0,0,0.8)] animate-countdown">
                {countdown}
              </span>
            </div>
          )}

          {/* Manual Trigger Button for Remote Mode */}
          {mode === 'remote' && countdown === null && (
            <div className="absolute bottom-6 inset-x-0 flex justify-center">
              <button
                onClick={() => setCountdown(3)}
                className="btn-favori-primary px-8 py-3 text-lg font-bold flex items-center gap-2 shadow-2xl cursor-pointer"
              >
                <Camera className="w-6 h-6" />
                Bấm chụp
              </button>
            </div>
          )}
        </div>
      </main>

      {/* Bottom Subtitle matching 8.png */}
      <footer className="relative z-10 w-full flex items-center justify-center pb-4 text-center">
        <p className="font-lobster text-2xl md:text-3xl text-[#1f1610]">
          Vui lòng đứng trong khung hình nhé
        </p>
      </footer>
    </div>
  );
};
