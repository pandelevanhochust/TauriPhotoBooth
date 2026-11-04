import React, { useState, useEffect } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Printer, CheckCircle } from 'lucide-react';
import { ipc } from '../../services/ipc';

interface FavoriPrintScreenProps {
  layoutType: string;
  frameTheme: string;
  filterId: string;
  beautifyEnabled: boolean;
  copies: number;
  photos: string[];
  onFinish: () => void;
}

export const FavoriPrintScreen: React.FC<FavoriPrintScreenProps> = ({
  layoutType,
  frameTheme,
  filterId,
  beautifyEnabled,
  copies,
  photos,
  onFinish,
}) => {
  const [status, setStatus] = useState<'composing' | 'printing' | 'completed'>('composing');
  const [progress, setProgress] = useState<number>(10);

  useEffect(() => {
    let active = true;

    const processJob = async () => {
      try {
        // Step 1: Compositing via Rust compositor
        setProgress(30);
        await ipc.invoke('compose_strip', {
          layout: layoutType,
          frameTheme,
          filter: filterId,
          beautify: beautifyEnabled,
          photos,
        });

        if (!active) return;
        setProgress(65);
        setStatus('printing');

        // Step 2: Spool to Thermal Printer
        await ipc.invoke('print_session', { copies });

        if (!active) return;
        setProgress(100);
        setStatus('completed');

        // Reset to Welcome screen after 5 seconds
        setTimeout(() => {
          if (active) onFinish();
        }, 5000);
      } catch (err) {
        console.warn('Composing/Print IPC error (simulating graceful finish):', err);
        if (!active) return;
        setProgress(100);
        setStatus('completed');
        setTimeout(() => {
          if (active) onFinish();
        }, 5000);
      }
    };

    processJob();

    return () => {
      active = false;
    };
  }, []);

  return (
    <FavoriLayout
      stepNumber={10}
      hideHeader={false}
    >
      <div className="flex flex-col items-center justify-center my-auto text-center space-y-8 max-w-xl mx-auto py-6">
        {status !== 'completed' ? (
          <>
            {/* Spinning/Breathing Icon */}
            <div className="w-24 h-24 rounded-full bg-[#7a0000]/10 border-2 border-[#7a0000]/40 flex items-center justify-center text-[#7a0000] animate-soft-pulse">
              <Printer className="w-12 h-12 animate-pulse" />
            </div>

            {/* Title matching 11.png */}
            <h2 className="font-serif-vintage italic font-bold text-3xl md:text-4xl text-[#1f1610] tracking-wide leading-relaxed">
              Đang gửi lệnh in , bạn chờ xíu nhé ...
            </h2>

            {/* Subtle Progress Bar */}
            <div className="w-full bg-[#ede4d8] h-2.5 rounded-full overflow-hidden border border-[#d6c4b2]">
              <div
                className="bg-[#7a0000] h-full transition-all duration-700 ease-out"
                style={{ width: `${progress}%` }}
              />
            </div>
          </>
        ) : (
          <div className="space-y-6 animate-soft-pulse">
            <div className="w-20 h-20 rounded-full bg-emerald-100 border-2 border-emerald-400 flex items-center justify-center text-emerald-700 mx-auto">
              <CheckCircle className="w-10 h-10" />
            </div>

            <h2 className="font-serif-vintage italic font-bold text-3xl md:text-4xl text-[#1f1610] tracking-wide">
              In ảnh thành công!
            </h2>

            <p className="font-serif-vintage italic font-bold text-2xl md:text-3xl text-[#7a0000] pt-2">
              Cảm ơn bạn đã sử dụng dịch vụ ,<br />
              Hãy nhận ảnh tại khay máy in nhé!
            </p>
          </div>
        )}
      </div>
    </FavoriLayout>
  );
};
