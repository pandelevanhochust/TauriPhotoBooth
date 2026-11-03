import React, { useState, useEffect } from 'react';
import { StandbyScreen } from './components/StandbyScreen';
import { LayoutSelectionScreen } from './components/LayoutSelectionScreen';
import type { LayoutConfig } from './components/LayoutSelectionScreen';
import { LiveViewScreen } from './components/LiveViewScreen';
import { ReviewScreen } from './components/ReviewScreen';
import { ComposingAndPrintScreen } from './components/ComposingAndPrintScreen';
import { AdminModal } from './components/AdminModal';
import { ipc } from './services/ipc';

export type KioskStep = 'standby' | 'layout' | 'capture' | 'review' | 'composing';

export const App: React.FC = () => {
  const [step, setStep] = useState<KioskStep>('standby');
  const [paperCount, setPaperCount] = useState<number>(350);
  const [kioskMode, setKioskMode] = useState<string>('FreeEvent');
  const [isAdminOpen, setIsAdminOpen] = useState<boolean>(false);

  // Session State
  const [layoutConfig, setLayoutConfig] = useState<LayoutConfig>({
    type: 'strip2x6',
    frameCount: 4,
    frameColor: '#ffffff',
    frameTheme: 'white',
  });
  const [capturedPhotos, setCapturedPhotos] = useState<string[]>([]);
  const [beautifyEnabled, setBeautifyEnabled] = useState<boolean>(true);

  // Initial load kiosk status from host
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

  const handleStartSession = async () => {
    try {
      await ipc.invoke('start_capture_session');
    } catch (e) {
      console.warn('Start session notification:', e);
    }
    setCapturedPhotos([]);
    setStep('layout');
  };

  const handleSelectLayout = (config: LayoutConfig) => {
    setLayoutConfig(config);
    setStep('capture');
  };

  const handleAllShotsCaptured = (photos: string[]) => {
    setCapturedPhotos(photos);
    setStep('review');
  };

  const handleRetakePhoto = (_indexToRetake: number) => {
    // Return to capture for retake
    setStep('capture');
  };

  const handleProceedToCompose = (beautify: boolean) => {
    setBeautifyEnabled(beautify);
    setStep('composing');
  };

  const handleFinishSession = () => {
    setCapturedPhotos([]);
    setStep('standby');
  };

  return (
    <div className="w-screen h-screen overflow-hidden bg-black select-none">
      {step === 'standby' && (
        <StandbyScreen
          onStartSession={handleStartSession}
          onOpenAdmin={() => setIsAdminOpen(true)}
          paperCount={paperCount}
          kioskMode={kioskMode}
        />
      )}

      {step === 'layout' && (
        <LayoutSelectionScreen
          onSelectLayout={handleSelectLayout}
          onCancel={() => setStep('standby')}
        />
      )}

      {step === 'capture' && (
        <LiveViewScreen
          totalShots={layoutConfig.frameCount}
          onAllShotsCaptured={handleAllShotsCaptured}
          onCancel={() => setStep('standby')}
        />
      )}

      {step === 'review' && (
        <ReviewScreen
          photos={capturedPhotos}
          maxRetakes={1}
          onRetakePhoto={handleRetakePhoto}
          onProceedToCompose={handleProceedToCompose}
        />
      )}

      {step === 'composing' && (
        <ComposingAndPrintScreen
          layout={layoutConfig}
          beautifyEnabled={beautifyEnabled}
          photos={capturedPhotos}
          onFinishSession={handleFinishSession}
        />
      )}

      {/* Admin Modal */}
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
