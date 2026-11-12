import React, { useState, useEffect } from 'react';
import { FavoriWelcomeScreen } from './components/favori/FavoriWelcomeScreen';
import { FavoriSizeScreen } from './components/favori/FavoriSizeScreen';
import type { PhotoSizeType } from './components/favori/FavoriSizeScreen';
import { FavoriFrameScreen } from './components/favori/FavoriFrameScreen';
import { FavoriQuantityScreen } from './components/favori/FavoriQuantityScreen';
import { FavoriModeScreen } from './components/favori/FavoriModeScreen';
import type { CaptureMode } from './components/favori/FavoriModeScreen';
import { FavoriCountdownScreen } from './components/favori/FavoriCountdownScreen';
import { FavoriLiveViewScreen } from './components/favori/FavoriLiveViewScreen';
import { FavoriFilterScreen } from './components/favori/FavoriFilterScreen';
import { FavoriPhotoSelectScreen } from './components/favori/FavoriPhotoSelectScreen';
import { FavoriPrintScreen } from './components/favori/FavoriPrintScreen';
import { AdminModal } from './components/AdminModal';
import { ipc } from './services/ipc';
import type { FrameOption, FilterOption } from './services/backendApi';

export type FavoriStep =
  | 'welcome'     // 1.png
  | 'size'        // 2.png (Bước 1/11)
  | 'frame'       // 3.png (Bước 2/11)
  | 'quantity'    // 4.png (Bước 3/11)
  // Step 4/11 (5.png - Thanh toán) is skipped as requested
  | 'mode'        // 6.png (Bước 5/11)
  | 'countdown'   // 7.png (Bước 6/11)
  | 'liveview'    // 8.png (Bước 7/11)
  | 'filter'      // 9.png (Bước 8/11)
  | 'photoSelect' // 10.png (Bước 9/11)
  | 'print';      // 11.png (Bước 10/11)

export const App: React.FC = () => {
  const [step, setStep] = useState<FavoriStep>('welcome');
  const [language, setLanguage] = useState<'vi' | 'en'>('vi');
  const [paperCount, setPaperCount] = useState<number>(350);
  const [kioskMode, setKioskMode] = useState<string>('FreeEvent');
  const [isAdminOpen, setIsAdminOpen] = useState<boolean>(false);

  // Session Options State
  const [selectedSize, setSelectedSize] = useState<PhotoSizeType>('strip2x6');
  const [selectedFrame, setSelectedFrame] = useState<string>('classic-cream');
  const [selectedFrameObj, setSelectedFrameObj] = useState<FrameOption | undefined>(undefined);
  const [quantity, setQuantity] = useState<number>(2);
  const [captureMode, setCaptureMode] = useState<CaptureMode>('autoCountdown');
  const [selectedFilter, setSelectedFilter] = useState<string>('original');
  const [selectedFilterObj, setSelectedFilterObj] = useState<FilterOption | undefined>(undefined);
  const [beautifyEnabled, setBeautifyEnabled] = useState<boolean>(true);
  const [capturedPhotos, setCapturedPhotos] = useState<string[]>([]);
  const [retakeTargetIndex, setRetakeTargetIndex] = useState<number | null>(null);

  // Fetch initial kiosk status from backend
  useEffect(() => {
    ipc.invoke<{ paperCount: number; mode: string }>('get_kiosk_status')
      .then((status) => {
        if (status) {
          if (typeof status.paperCount === 'number') setPaperCount(status.paperCount);
          if (status.mode) setKioskMode(status.mode);
        }
      })
      .catch((err) => console.log('Get kiosk status error:', err));
  }, []);

  // Keyboard shortcuts for technician admin access (F2, Ctrl+Shift+A, Ctrl+Alt+A)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const isA = e.key.toLowerCase() === 'a';
      if (
        (e.ctrlKey && e.shiftKey && isA) ||
        (e.ctrlKey && e.altKey && isA) ||
        e.key === 'F2'
      ) {
        e.preventDefault();
        setIsAdminOpen((prev) => !prev);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    const unlistenIpc = ipc.listen('open_admin_modal', () => {
      setIsAdminOpen(true);
    });
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      unlistenIpc();
    };
  }, []);

  const handleStartSession = () => {
    ipc.invoke('start_capture_session').catch((e) => console.warn(e));
    setCapturedPhotos([]);
    setRetakeTargetIndex(null);
    setStep('size');
  };

  const handleRetakeShot = (retakeIndex: number) => {
    setRetakeTargetIndex(retakeIndex);
    setStep('liveview');
  };

  const handleFinishSession = () => {
    setCapturedPhotos([]);
    setRetakeTargetIndex(null);
    setQuantity(2);
    setStep('welcome');
  };

  return (
    <div className="w-screen h-screen overflow-hidden bg-[#f5f0ea] select-none">
      {/* Step 0: Welcome Screen (1.png) */}
      {step === 'welcome' && (
        <FavoriWelcomeScreen
          onStart={handleStartSession}
          onOpenAdmin={() => setIsAdminOpen(true)}
          language={language}
          onLanguageChange={setLanguage}
          paperCount={paperCount}
        />
      )}

      {/* Step 1/11: Chọn kích cỡ ảnh (2.png) */}
      {step === 'size' && (
        <FavoriSizeScreen
          selectedSize={selectedSize}
          onSelectSize={setSelectedSize}
          onNext={() => setStep('frame')}
          onBack={() => setStep('welcome')}
        />
      )}

      {/* Step 2/11: Chọn khung ảnh (3.png) */}
      {step === 'frame' && (
        <FavoriFrameScreen
          selectedSize={selectedSize}
          selectedFrame={selectedFrame}
          onSelectFrame={(id, frameObj) => {
            setSelectedFrame(id);
            if (frameObj) {
              setSelectedFrameObj(frameObj);
              if (frameObj.layoutId === '1x4') setSelectedSize('strip2x6');
              else if (frameObj.layoutId === '2x2') setSelectedSize('postcard4x6');
              else if (frameObj.layoutId === '2x3') setSelectedSize('special4x6');
            }
          }}
          onNext={() => setStep('quantity')}
          onBack={() => setStep('size')}
        />
      )}

      {/* Step 3/11: Chọn số lượng in & mã giảm giá (4.png) */}
      {/* NOTE: Step 4/11 (Payment 5.png) is skipped -> Next goes directly to Step 5/11 (mode 6.png) */}
      {step === 'quantity' && (
        <FavoriQuantityScreen
          quantity={quantity}
          onQuantityChange={setQuantity}
          pricePerUnit={50000}
          isFreeMode={kioskMode === 'FreeEvent'}
          onNext={() => setStep('mode')}
          onBack={() => setStep('frame')}
        />
      )}

      {/* Step 5/11: Chọn chế độ chụp (6.png) */}
      {step === 'mode' && (
        <FavoriModeScreen
          selectedMode={captureMode}
          onSelectMode={(m) => {
            setCaptureMode(m);
            setStep('countdown');
          }}
          onNext={() => setStep('countdown')}
          onBack={() => setStep('quantity')}
        />
      )}

      {/* Step 6/11: Chuẩn bị chụp (7.png) */}
      {step === 'countdown' && (
        <FavoriCountdownScreen
          onReady={() => setStep('liveview')}
        />
      )}

      {/* Step 7/11: Chụp ảnh & Live View (8.png) */}
      {step === 'liveview' && (
        <FavoriLiveViewScreen
          totalShots={retakeTargetIndex !== null ? 1 : 6}
          mode={captureMode}
          onAllShotsCaptured={(newPhotos) => {
            if (retakeTargetIndex !== null && newPhotos.length > 0) {
              const updated = [...capturedPhotos];
              updated[retakeTargetIndex] = newPhotos[0];
              setCapturedPhotos(updated);
              setRetakeTargetIndex(null);
              setStep('photoSelect');
            } else {
              setCapturedPhotos(newPhotos);
              setStep('filter');
            }
          }}
          onCancel={() => setStep('welcome')}
        />
      )}

      {/* Step 8/11: Chọn bộ lọc (9.png) */}
      {step === 'filter' && (
        <FavoriFilterScreen
          photos={capturedPhotos}
          selectedFilter={selectedFilter}
          onSelectFilter={(id, filterObj) => {
            setSelectedFilter(id);
            if (filterObj) setSelectedFilterObj(filterObj);
          }}
          beautifyEnabled={beautifyEnabled}
          onToggleBeautify={setBeautifyEnabled}
          onNext={() => setStep('photoSelect')}
          onBack={() => setStep('mode')}
        />
      )}

      {/* Step 9/11: Chọn ảnh để in (10.png) */}
      {step === 'photoSelect' && (
        <FavoriPhotoSelectScreen
          photos={capturedPhotos}
          frameObj={selectedFrameObj}
          layoutType={selectedFrameObj?.layoutId || selectedSize}
          filterCss={selectedFilterObj?.cssFilter}
          onConfirmSelection={(slotted) => {
            setCapturedPhotos(slotted);
          }}
          onRetake={handleRetakeShot}
          onNext={() => setStep('print')}
          onBack={() => setStep('filter')}
        />
      )}

      {/* Step 10/11: In ảnh (11.png) */}
      {/* NOTE: Step 11/11 (QR download 12.png) is skipped -> finishes and resets to Welcome */}
      {step === 'print' && (
        <FavoriPrintScreen
          layoutType={selectedFrameObj?.layoutId || selectedSize}
          frameTheme={selectedFrame}
          frameObj={selectedFrameObj}
          filterId={selectedFilter}
          filterObj={selectedFilterObj}
          beautifyEnabled={beautifyEnabled}
          copies={quantity}
          photos={capturedPhotos}
          onFinish={handleFinishSession}
        />
      )}

      {/* Administrative PIN Modal */}
      <AdminModal
        isOpen={isAdminOpen}
        onClose={() => setIsAdminOpen(false)}
        currentPaperCount={paperCount}
        kioskMode={kioskMode}
        onUpdateStatus={(newPaper, newMode) => {
          setPaperCount(newPaper);
          setKioskMode(newMode);
        }}
      />
    </div>
  );
};

export default App;
