import React, { useState, useEffect } from 'react';
import { FavoriWelcomeScreen } from './components/favori/FavoriWelcomeScreen';
import { FavoriSizeScreen } from './components/favori/FavoriSizeScreen';
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
import { backendApi, priceForQuantity, smallestHoleAspect } from './services/backendApi';
import { preloadLuts } from './services/lut';
import { preloadSkin, type SkinLevel } from './services/skin';
import type { FrameOption, FilterOption, LayoutOption } from './services/backendApi';
import { getKioskSettings, type KioskSettings } from './services/kioskSettings';
import type { PhotoTransform } from './services/photoTransform';

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
  const [settings, setSettings] = useState<KioskSettings>(() => getKioskSettings());
  const [selectedLayout, setSelectedLayout] = useState<LayoutOption | undefined>(undefined);
  const [selectedFrame, setSelectedFrame] = useState<string>('');
  const [selectedFrameObj, setSelectedFrameObj] = useState<FrameOption | undefined>(undefined);
  const [quantity, setQuantity] = useState<number>(2);
  const [captureMode, setCaptureMode] = useState<CaptureMode>('autoCountdown');
  const [selectedFilter, setSelectedFilter] = useState<string>('original');
  const [selectedFilterObj, setSelectedFilterObj] = useState<FilterOption | undefined>(undefined);
  // Thử nghiệm: độ làm mịn da 0-100 khách chọn ở bước 8 (chỉ có tác dụng khi bật trong Admin)
  const [selectedSkin, setSelectedSkin] = useState<SkinLevel>(0);
  // capturedPhotos: toàn bộ ảnh gốc của phiên; slottedPhotos: ảnh đã xếp theo thứ tự ô để in
  const [capturedPhotos, setCapturedPhotos] = useState<string[]>([]);
  const [slottedPhotos, setSlottedPhotos] = useState<string[]>([]);
  // Đường dẫn ảnh gốc trên máy (ảnh DSLR độ phân giải đầy đủ), cùng thứ tự với capturedPhotos / slottedPhotos
  const [capturedPaths, setCapturedPaths] = useState<string[]>([]);
  const [slottedPaths, setSlottedPaths] = useState<string[]>([]);
  const [slotTransforms, setSlotTransforms] = useState<PhotoTransform[]>([]);

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

  // Nạp sẵn các bộ lọc LUT ngay lúc khởi động (và khi CMS đổi bộ lọc): tới bước chọn bộ lọc chỉ còn áp lên ảnh
  useEffect(() => {
    const preload = () =>
      backendApi
        .getPresets()
        .then(preloadLuts)
        .catch((err) => console.warn('[App] Nạp sẵn LUT lỗi:', err));
    // Frame mới: tải sẵn ảnh thu nhỏ vào bộ nhớ đệm của WebView để bước chọn frame hiện ngay
    const preloadFrameThumbs = () =>
      backendApi
        .getFrames()
        .then((frames) =>
          frames.forEach((f) => {
            const img = new Image();
            img.src = f.thumbnailUrl || f.frameUrl;
          }),
        )
        .catch((err) => console.warn('[App] Tải sẵn ảnh frame lỗi:', err));
    preload();
    return ipc.listen<{ key?: string }>('cms_updated', (data) => {
      if (data?.key === 'presets') preload();
      if (data?.key === 'frames') preloadFrameThumbs();
    });
  }, []);

  // Thử nghiệm làm mịn da: nạp sẵn mô hình nhận diện khuôn mặt lúc khởi động để lần đầu không phải chờ
  useEffect(() => {
    if (settings.skinSmoothEnabled) void preloadSkin();
  }, [settings.skinSmoothEnabled]);

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
    setSettings(getKioskSettings());
    setCapturedPhotos([]);
    setSlottedPhotos([]);
    setCapturedPaths([]);
    setSlottedPaths([]);
    setSlotTransforms([]);
    setStep('size');
  };

  const handleFinishSession = () => {
    setCapturedPhotos([]);
    setSlottedPhotos([]);
    setCapturedPaths([]);
    setSlottedPaths([]);
    setSlotTransforms([]);
    setQuantity(2);
    setSelectedLayout(undefined);
    setSelectedFrame('');
    setSelectedFrameObj(undefined);
    setSelectedFilter('original');
    setSelectedFilterObj(undefined);
    setSelectedSkin(0);
    setStep('welcome');
    // Hết phiên: hỏi server frame và bộ lọc ngầm, tải sẵn file mới để khách sau thấy ngay bản mới (layout giữ nguyên)
    ipc.invoke('refresh_cms').catch((e) => console.warn('[App] refresh_cms lỗi:', e));
  };

  const handleSelectLayout = (layout: LayoutOption) => {
    if (layout.id !== selectedLayout?.id) {
      // Frame phụ thuộc layout: đổi layout thì bỏ frame đang chọn
      setSelectedFrame('');
      setSelectedFrameObj(undefined);
    }
    setSelectedLayout(layout);
  };

  const isFreeMode = kioskMode === 'FreeEvent';

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
          selectedLayoutId={selectedLayout?.id}
          onSelectLayout={handleSelectLayout}
          showPrice={!isFreeMode}
          onNext={() => setStep('frame')}
          onBack={() => setStep('welcome')}
        />
      )}

      {/* Step 2/11: Chọn khung ảnh (3.png) */}
      {step === 'frame' && (
        <FavoriFrameScreen
          layout={selectedLayout}
          selectedFrame={selectedFrame}
          onSelectFrame={(id, frameObj) => {
            setSelectedFrame(id);
            setSelectedFrameObj(frameObj);
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
          totalPrice={selectedLayout ? priceForQuantity(selectedLayout, quantity) : undefined}
          isFreeMode={isFreeMode}
          onNext={() => setStep('mode')}
          onBack={() => setStep('frame')}
        />
      )}

      {/* Step 5/11: Chọn chế độ chụp (6.png) */}
      {step === 'mode' && (
        <FavoriModeScreen
          selectedMode={captureMode}
          remoteTimeoutSec={settings.remoteCaptureTimeoutSec}
          countdownSec={settings.autoCountdownSec}
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
          totalShots={settings.totalShots}
          mode={captureMode}
          countdownSec={settings.autoCountdownSec}
          remoteTimeoutSec={settings.remoteCaptureTimeoutSec}
          timelapseEnabled={settings.timelapseEnabled}
          showShutterButton={settings.screenShutterButton}
          mirrorLiveView={settings.mirrorLiveView}
          guideAspect={smallestHoleAspect(selectedFrameObj?.holes)}
          onAllShotsCaptured={(newPhotos, paths) => {
            setCapturedPhotos(newPhotos);
            setCapturedPaths(paths);
            setStep('filter');
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
          timeoutSec={settings.filterTimeoutSec}
          onNext={() => setStep('photoSelect')}
          skinEnabled={settings.skinSmoothEnabled}
          skin={settings.skinSmoothEnabled ? selectedSkin : 0}
          onSelectSkin={setSelectedSkin}
        />
      )}

      {/* Step 9/11: Chọn ảnh để in (10.png) */}
      {step === 'photoSelect' && (
        <FavoriPhotoSelectScreen
          photos={capturedPhotos}
          frameObj={selectedFrameObj}
          layout={selectedLayout}
          filter={selectedFilterObj}
          skin={settings.skinSmoothEnabled ? selectedSkin : 0}
          timeoutSec={settings.selectTimeoutSec}
          onBack={() => setStep('filter')}
          onConfirm={(slotted, transforms) => {
            setSlottedPhotos(slotted);
            setSlottedPaths(slotted.map((p) => capturedPaths[capturedPhotos.indexOf(p)] || ''));
            setSlotTransforms(transforms);
            setStep('print');
          }}
        />
      )}

      {/* Step 10/11: In ảnh (11.png) */}
      {/* NOTE: Step 11/11 (QR download 12.png) is skipped -> finishes and resets to Welcome */}
      {step === 'print' && (
        <FavoriPrintScreen
          layoutType={selectedFrameObj?.layoutId || selectedLayout?.id || '2x2'}
          frameTheme={selectedFrame}
          frameObj={selectedFrameObj}
          filterId={selectedFilter}
          filterObj={selectedFilterObj}
          beautifyEnabled={false}
          copies={quantity}
          photos={slottedPhotos}
          photoPaths={slottedPaths}
          transforms={slotTransforms}
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
