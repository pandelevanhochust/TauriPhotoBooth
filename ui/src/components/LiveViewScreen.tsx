import React, { useState, useEffect, useRef } from 'react';
import { FlipHorizontal, Camera } from 'lucide-react';
import { ipc } from '../services/ipc';

interface LiveViewScreenProps {
  totalShots?: number;
  onAllShotsCaptured: (capturedImages: string[]) => void;
  onCancel: () => void;
}

export const LiveViewScreen: React.FC<LiveViewScreenProps> = ({
  totalShots = 4,
  onAllShotsCaptured,
  onCancel,
}) => {
  const [currentShot, setCurrentShot] = useState(1);
  const [countdown, setCountdown] = useState<number | null>(5);
  const [isMirrored, setIsMirrored] = useState(true);
  const [capturedImages, setCapturedImages] = useState<string[]>([]);
  const [isFlashing, setIsFlashing] = useState(false);
  const [liveFrameBase64, setLiveFrameBase64] = useState<string | null>(null);

  const countdownIntervalRef = useRef<any>(null);
  const audioContextRef = useRef<AudioContext | null>(null);

  // Play audio beeps using native Web Audio Synthesizer
  const playBeep = (freq: number = 880, durationMs: number = 100) => {
    try {
      if (!audioContextRef.current) {
        audioContextRef.current = new (window.AudioContext || (window as any).webkitAudioContext)();
      }
      const ctx = audioContextRef.current;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq, ctx.currentTime);
      gain.gain.setValueAtTime(0.3, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + durationMs / 1000);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + durationMs / 1000);
    } catch {
      // Audio might be blocked if user hasn't interacted
    }
  };

  // Listen to live stream frames from C# CameraService
  useEffect(() => {
    const unsubscribe = ipc.on('camera_frame', (data: { base64: string }) => {
      if (data?.base64) {
        setLiveFrameBase64(`data:image/jpeg;base64,${data.base64}`);
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  // Manage Countdown and Capture Sequence
  useEffect(() => {
    startCountdown();
    return () => {
      if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);
    };
  }, [currentShot]);

  const startCountdown = () => {
    setCountdown(5);
    if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);

    countdownIntervalRef.current = setInterval(() => {
      setCountdown((prev) => {
        if (prev === null) return 5;
        if (prev <= 1) {
          if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);
          triggerCapture();
          return null;
        }
        playBeep(prev === 2 ? 1046 : 880, 120); // Higher pitch near end
        return prev - 1;
      });
    }, 1000);
  };

  const triggerCapture = async () => {
    // Play shutter sound / higher frequency
    playBeep(1760, 250);
    
    // Trigger visual screen flash
    setIsFlashing(true);
    setTimeout(() => setIsFlashing(false), 200);

    try {
      const res = await ipc.invoke<{ filePath: string; base64?: string }>('capture_shot', {
        shotIndex: currentShot,
      });

      const shotImg = res?.base64 
        ? `data:image/jpeg;base64,${res.base64}` 
        : (liveFrameBase64 || `https://picsum.photos/seed/hihi_${Date.now()}/800/600`);

      const updated = [...capturedImages, shotImg];
      setCapturedImages(updated);

      if (currentShot < totalShots) {
        setTimeout(() => {
          setCurrentShot(currentShot + 1);
        }, 1200);
      } else {
        setTimeout(() => {
          onAllShotsCaptured(updated);
        }, 1000);
      }
    } catch (err) {
      console.error('Capture shot error:', err);
      // Fallback in case of mock failure
      const fallbackImg = `https://picsum.photos/seed/hihi_fallback_${currentShot}/800/600`;
      const updated = [...capturedImages, fallbackImg];
      setCapturedImages(updated);
      if (currentShot < totalShots) {
        setCurrentShot(currentShot + 1);
      } else {
        onAllShotsCaptured(updated);
      }
    }
  };

  return (
    <div className="relative w-screen h-screen bg-black flex flex-col justify-between overflow-hidden select-none">
      {/* Visual Flash effect */}
      {isFlashing && (
        <div className="absolute inset-0 bg-white z-50 pointer-events-none transition-opacity duration-150 animate-out fade-out" />
      )}

      {/* Main Viewport Container */}
      <div className="relative w-full h-full flex items-center justify-center overflow-hidden">
        {/* Live Camera Feed Image */}
        {liveFrameBase64 ? (
          <img
            src={liveFrameBase64}
            alt="Live View"
            className={`w-full h-full object-cover transition-transform duration-200 ${
              isMirrored ? 'scale-x-[-1]' : ''
            }`}
          />
        ) : (
          /* Simulated high-tech viewfinder background */
          <div className="relative w-full h-full bg-gradient-to-b from-gray-900 to-black flex items-center justify-center">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(99,102,241,0.15)_0%,transparent_70%)]"></div>
            
            {/* Camera Guide Grid Overlay */}
            <div className="w-[80vw] h-[80vh] border-2 border-white/20 rounded-3xl relative flex items-center justify-center">
              <div className="w-16 h-16 border-t-2 border-l-2 border-rose-500 absolute top-4 left-4"></div>
              <div className="w-16 h-16 border-t-2 border-r-2 border-rose-500 absolute top-4 right-4"></div>
              <div className="w-16 h-16 border-b-2 border-l-2 border-rose-500 absolute bottom-4 left-4"></div>
              <div className="w-16 h-16 border-b-2 border-r-2 border-rose-500 absolute bottom-4 right-4"></div>
              
              <div className="text-white/40 flex flex-col items-center gap-3">
                <Camera className="w-16 h-16 animate-pulse" />
                <span className="text-xl tracking-widest font-mono">LIVE VIEW READY</span>
              </div>
            </div>
          </div>
        )}

        {/* Large Countdown Overlay Ring & Digits */}
        {countdown !== null && (
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none z-30">
            <div className="relative w-48 h-48 rounded-full bg-black/60 backdrop-blur-xl border-4 border-rose-500/80 flex items-center justify-center shadow-[0_0_50px_rgba(244,63,94,0.6)]">
              <span className="text-8xl font-black text-white animate-countdown-pop drop-shadow-lg">
                {countdown}
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Top Floating Controls */}
      <div className="absolute top-8 left-8 right-8 flex justify-between items-center z-40 pointer-events-auto">
        <button
          onClick={onCancel}
          className="px-5 py-2.5 rounded-full bg-black/60 text-white font-medium text-sm backdrop-blur-md border border-white/20 active:scale-95 transition"
        >
          Hủy phiên
        </button>

        {/* Shot Counter Pill */}
        <div className="px-6 py-2.5 rounded-full bg-black/60 border border-white/20 backdrop-blur-md flex items-center gap-3">
          <span className="w-3 h-3 rounded-full bg-rose-500 animate-pulse"></span>
          <span className="text-white font-bold text-lg tracking-wider">
            ẢNH {currentShot} / {totalShots}
          </span>
        </div>

        {/* Mirror Flip Toggle */}
        <button
          onClick={() => setIsMirrored(!isMirrored)}
          className={`flex items-center gap-2 px-5 py-2.5 rounded-full backdrop-blur-md border transition active:scale-95 ${
            isMirrored 
              ? 'bg-rose-500/80 border-rose-400 text-white shadow-lg' 
              : 'bg-black/60 border-white/20 text-gray-300'
          }`}
        >
          <FlipHorizontal className="w-5 h-5" />
          <span className="text-sm font-semibold">{isMirrored ? 'Lật gương: BẬT' : 'Lật gương: TẮT'}</span>
        </button>
      </div>

      {/* Bottom Floating Film Strip Preview */}
      <div className="absolute bottom-8 left-1/2 -translate-x-1/2 z-40 flex items-center gap-4 bg-black/60 px-6 py-3.5 rounded-3xl border border-white/20 backdrop-blur-md pointer-events-auto">
        {Array.from({ length: totalShots }).map((_, idx) => {
          const img = capturedImages[idx];
          const isDone = !!img;
          const isCurrent = idx + 1 === currentShot;

          return (
            <div
              key={idx}
              className={`w-14 h-18 rounded-xl overflow-hidden border-2 transition-all flex items-center justify-center ${
                isCurrent
                  ? 'border-rose-500 shadow-[0_0_15px_rgba(244,63,94,0.8)] scale-110'
                  : isDone
                  ? 'border-white/60'
                  : 'border-white/10 bg-white/5'
              }`}
            >
              {img ? (
                <img src={img} alt={`Shot ${idx + 1}`} className="w-full h-full object-cover" />
              ) : (
                <span className="text-xs font-bold text-gray-500">{idx + 1}</span>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};
