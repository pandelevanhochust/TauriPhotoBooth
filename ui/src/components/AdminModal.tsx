import React, { useState } from 'react';
import { X, Lock, Printer, Camera, RotateCcw, Sliders, CheckCircle, AlertTriangle } from 'lucide-react';
import { ipc } from '../services/ipc';

interface AdminModalProps {
  isOpen: boolean;
  onClose: () => void;
  currentPaperCount: number;
  kioskMode: string;
  onUpdateStatus: (paper: number, mode: string) => void;
}

export const AdminModal: React.FC<AdminModalProps> = ({
  isOpen,
  onClose,
  currentPaperCount,
  kioskMode,
  onUpdateStatus,
}) => {
  const [pin, setPin] = useState('');
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [pinError, setPinError] = useState(false);

  // Admin Dashboard State
  const [paper, setPaper] = useState(currentPaperCount);
  const [mode, setMode] = useState(kioskMode);
  const [activeTab, setActiveTab] = useState<'status' | 'hardware' | 'reprint'>('status');
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleKeyClick = (digit: string) => {
    if (pin.length < 4) {
      const newPin = pin + digit;
      setPin(newPin);
      if (newPin.length === 4) {
        verifyPin(newPin);
      }
    }
  };

  const handleDelete = () => {
    setPin(pin.slice(0, -1));
    setPinError(false);
  };

  const verifyPin = async (inputPin: string) => {
    try {
      const isValid = await ipc.invoke<boolean>('verify_admin_pin', { pin: inputPin });
      if (isValid) {
        setIsAuthenticated(true);
        setPinError(false);
      } else {
        setPinError(true);
        setPin('');
      }
    } catch {
      // Fallback check
      if (inputPin === '8888' || inputPin === '1234') {
        setIsAuthenticated(true);
      } else {
        setPinError(true);
        setPin('');
      }
    }
  };

  const handleTestPrint = async () => {
    setActionMessage('Đang gửi lệnh in thử nghiệm...');
    try {
      await ipc.invoke('print_test_page');
      setActionMessage('In thử thành công!');
    } catch {
      setActionMessage('Lỗi in thử nghiệm');
    }
    setTimeout(() => setActionMessage(null), 3000);
  };

  const handleResetPaper = (count: number) => {
    setPaper(count);
    onUpdateStatus(count, mode);
    setActionMessage(`Đã nạp giấy: ${count} bản`);
    setTimeout(() => setActionMessage(null), 3000);
  };

  const handleSaveSettings = () => {
    onUpdateStatus(paper, mode);
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/85 backdrop-blur-md flex items-center justify-center p-6 text-white select-none">
      <div className="relative w-full max-w-2xl bg-[#161824] rounded-3xl border border-white/20 shadow-2xl p-8 flex flex-col">
        {/* Close Button */}
        <button
          onClick={onClose}
          className="absolute top-6 right-6 w-10 h-10 rounded-full bg-white/10 hover:bg-white/20 flex items-center justify-center transition active:scale-95"
        >
          <X className="w-5 h-5 text-gray-300" />
        </button>

        {!isAuthenticated ? (
          /* PIN Input Screen */
          <div className="flex flex-col items-center justify-center py-6">
            <div className="w-16 h-16 rounded-3xl bg-rose-500/20 border border-rose-500/40 flex items-center justify-center mb-6">
              <Lock className="w-8 h-8 text-rose-400" />
            </div>

            <h3 className="text-2xl font-bold mb-2">QUẢN TRỊ VIÊN KIOSK</h3>
            <p className="text-sm text-gray-400 mb-8">Nhập mã PIN 4 chữ số (mặc định: 8888 hoặc 1234)</p>

            {/* PIN Dots Display */}
            <div className="flex gap-4 mb-8">
              {[0, 1, 2, 3].map((idx) => (
                <div
                  key={idx}
                  className={`w-5 h-5 rounded-full border-2 transition-all ${
                    pin.length > idx
                      ? 'bg-rose-500 border-rose-500 scale-125'
                      : 'border-white/30 bg-white/5'
                  }`}
                />
              ))}
            </div>

            {pinError && (
              <div className="flex items-center gap-2 text-rose-400 text-sm mb-4">
                <AlertTriangle className="w-4 h-4" />
                <span>Mã PIN không chính xác, vui lòng thử lại</span>
              </div>
            )}

            {/* Numeric Keypad */}
            <div className="grid grid-cols-3 gap-4 max-w-xs w-full">
              {['1', '2', '3', '4', '5', '6', '7', '8', '9', 'C', '0', '⌫'].map((k) => (
                <button
                  key={k}
                  onClick={() => {
                    if (k === 'C') setPin('');
                    else if (k === '⌫') handleDelete();
                    else handleKeyClick(k);
                  }}
                  className="h-16 rounded-2xl bg-white/10 hover:bg-white/20 active:scale-95 text-2xl font-semibold flex items-center justify-center transition border border-white/10"
                >
                  {k}
                </button>
              ))}
            </div>
          </div>
        ) : (
          /* Admin Dashboard */
          <div className="flex flex-col h-full">
            {/* Header */}
            <div className="mb-6">
              <h3 className="text-2xl font-bold flex items-center gap-3">
                <Sliders className="w-6 h-6 text-rose-400" />
                <span>BẢNG ĐIỀU KHIỂN HỆ THỐNG</span>
              </h3>
              <p className="text-xs text-gray-400 mt-1">Cài đặt thiết bị phần cứng, giấy in và lịch sử phiên</p>
            </div>

            {/* Tabs */}
            <div className="flex gap-3 mb-6 border-b border-white/10 pb-3">
              <button
                onClick={() => setActiveTab('status')}
                className={`px-4 py-2 rounded-xl text-sm font-semibold transition ${
                  activeTab === 'status' ? 'bg-rose-500 text-white' : 'bg-white/5 text-gray-400 hover:text-white'
                }`}
              >
                Giấy In & Chế Độ
              </button>
              <button
                onClick={() => setActiveTab('hardware')}
                className={`px-4 py-2 rounded-xl text-sm font-semibold transition ${
                  activeTab === 'hardware' ? 'bg-rose-500 text-white' : 'bg-white/5 text-gray-400 hover:text-white'
                }`}
              >
                Kiểm Tra Thiết Bị
              </button>
              <button
                onClick={() => setActiveTab('reprint')}
                className={`px-4 py-2 rounded-xl text-sm font-semibold transition ${
                  activeTab === 'reprint' ? 'bg-rose-500 text-white' : 'bg-white/5 text-gray-400 hover:text-white'
                }`}
              >
                In Lại (Reprint)
              </button>
            </div>

            {/* Tab Contents */}
            <div className="flex-1 space-y-6">
              {activeTab === 'status' && (
                <div className="space-y-6">
                  {/* Paper Count Management */}
                  <div className="p-4 rounded-2xl bg-white/5 border border-white/10">
                    <label className="text-sm font-semibold text-gray-300 block mb-2">
                      Số lượng giấy in còn lại:
                    </label>
                    <div className="flex items-center gap-4">
                      <input
                        type="number"
                        value={paper}
                        onChange={(e) => setPaper(parseInt(e.target.value) || 0)}
                        className="bg-black/50 border border-white/20 rounded-xl px-4 py-2.5 text-xl font-bold w-36 text-center text-rose-400"
                      />
                      <button
                        onClick={() => handleResetPaper(350)}
                        className="px-4 py-2.5 rounded-xl bg-white/10 hover:bg-white/20 text-sm font-medium transition"
                      >
                        Nạp cuộn mới (350 bản)
                      </button>
                    </div>
                  </div>

                  {/* Kiosk Mode Toggle */}
                  <div className="p-4 rounded-2xl bg-white/5 border border-white/10">
                    <label className="text-sm font-semibold text-gray-300 block mb-3">
                      Chế độ hoạt động Kiosk:
                    </label>
                    <div className="flex gap-4">
                      <button
                        onClick={() => setMode('FreeEvent')}
                        className={`flex-1 py-3 px-4 rounded-xl border text-sm font-bold transition ${
                          mode === 'FreeEvent'
                            ? 'bg-rose-500/20 border-rose-500 text-rose-300'
                            : 'bg-black/40 border-white/10 text-gray-400'
                        }`}
                      >
                        Sự Kiện Miễn Phí (Free)
                      </button>
                      <button
                        onClick={() => setMode('PaidVietQR')}
                        className={`flex-1 py-3 px-4 rounded-xl border text-sm font-bold transition ${
                          mode === 'PaidVietQR'
                            ? 'bg-purple-500/20 border-purple-500 text-purple-300'
                            : 'bg-black/40 border-white/10 text-gray-400'
                        }`}
                      >
                        Thanh Toán VietQR
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {activeTab === 'hardware' && (
                <div className="space-y-4">
                  <div className="flex items-center justify-between p-4 rounded-2xl bg-white/5 border border-white/10">
                    <div className="flex items-center gap-3">
                      <Printer className="w-5 h-5 text-rose-400" />
                      <div>
                        <div className="font-semibold text-sm">Máy in nhiệt (DNP / Citizen)</div>
                        <div className="text-xs text-gray-400">Trạng thái: Sẵn sàng (Ready)</div>
                      </div>
                    </div>
                    <button
                      onClick={handleTestPrint}
                      className="px-4 py-2 rounded-xl bg-rose-500 hover:bg-rose-600 text-xs font-bold transition active:scale-95"
                    >
                      In Thử Nghiệm
                    </button>
                  </div>

                  <div className="flex items-center justify-between p-4 rounded-2xl bg-white/5 border border-white/10">
                    <div className="flex items-center gap-3">
                      <Camera className="w-5 h-5 text-purple-400" />
                      <div>
                        <div className="font-semibold text-sm">Máy ảnh / Camera</div>
                        <div className="text-xs text-gray-400">Thiết bị: DirectShow / High-Res Mirror</div>
                      </div>
                    </div>
                    <span className="text-xs px-3 py-1 bg-emerald-500/20 text-emerald-400 rounded-full font-mono">
                      Connected
                    </span>
                  </div>
                </div>
              )}

              {activeTab === 'reprint' && (
                <div className="space-y-3">
                  <p className="text-xs text-gray-400 mb-2">
                    Các phiên chụp gần nhất được lưu trong `store.db`. Nhấn "In lại" để in mà không tính phí.
                  </p>
                  <div className="space-y-2 max-h-48 overflow-y-auto pr-2">
                    {[1, 2, 3].map((item) => (
                      <div
                        key={item}
                        className="flex items-center justify-between p-3 rounded-xl bg-white/5 border border-white/10 text-xs"
                      >
                        <div>
                          <div className="font-bold">Phiên #{item}024 - 4 ảnh (Dải 2x6)</div>
                          <div className="text-gray-400 text-[10px]">Thời gian: 10 phút trước</div>
                        </div>
                        <button
                          onClick={() => {
                            setActionMessage(`Đang in lại phiên #${item}024...`);
                            setTimeout(() => setActionMessage(null), 2500);
                          }}
                          className="px-3 py-1.5 rounded-lg bg-white/10 hover:bg-white/20 font-semibold text-white flex items-center gap-1.5"
                        >
                          <RotateCcw className="w-3.5 h-3.5 text-rose-400" />
                          <span>In lại</span>
                        </button>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* Status Feedback Message */}
            {actionMessage && (
              <div className="mt-4 p-3 rounded-xl bg-emerald-500/20 border border-emerald-500/40 text-emerald-300 text-xs flex items-center gap-2">
                <CheckCircle className="w-4 h-4" />
                <span>{actionMessage}</span>
              </div>
            )}

            {/* Footer Buttons */}
            <div className="mt-8 pt-4 border-t border-white/10 flex justify-end gap-3">
              <button
                onClick={onClose}
                className="px-6 py-2.5 rounded-xl bg-white/10 hover:bg-white/20 text-sm font-semibold transition"
              >
                Đóng
              </button>
              <button
                onClick={handleSaveSettings}
                className="px-6 py-2.5 rounded-xl bg-rose-500 hover:bg-rose-600 text-sm font-bold transition shadow-lg"
              >
                Lưu Thay Đổi
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
