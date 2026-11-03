import React, { useState } from 'react';
import { Columns, Square, Palette, ArrowRight, ArrowLeft } from 'lucide-react';

export interface LayoutConfig {
  type: 'strip2x6' | 'postcard4x6';
  frameCount: number;
  frameColor: string;
  frameTheme: string;
}

interface LayoutSelectionScreenProps {
  onSelectLayout: (config: LayoutConfig) => void;
  onCancel: () => void;
}

const THEMES = [
  { id: 'white', name: 'Trắng Tối Giản', bg: '#ffffff', text: '#111827', border: '#e5e7eb' },
  { id: 'black', name: 'Đen Sang Trọng', bg: '#18181b', text: '#f4f4f5', border: '#3f3f46' },
  { id: 'pink', name: 'Hồng Ngọt Ngào', bg: '#fdf2f8', text: '#db2777', border: '#fbcfe8' },
  { id: 'retro', name: 'Retro Vintage', bg: '#fef3c7', text: '#92400e', border: '#fde68a' },
];

export const LayoutSelectionScreen: React.FC<LayoutSelectionScreenProps> = ({
  onSelectLayout,
  onCancel,
}) => {
  const [selectedType, setSelectedType] = useState<'strip2x6' | 'postcard4x6'>('strip2x6');
  const [selectedTheme, setSelectedTheme] = useState(THEMES[0]);

  const handleConfirm = () => {
    onSelectLayout({
      type: selectedType,
      frameCount: 4,
      frameColor: selectedTheme.bg,
      frameTheme: selectedTheme.id,
    });
  };

  return (
    <div className="relative w-screen h-screen bg-gradient-to-br from-[#0c0d14] via-[#161726] to-[#0c0d14] flex flex-col justify-between p-10 select-none overflow-hidden">
      {/* Top Header */}
      <div className="flex items-center justify-between z-10">
        <button
          onClick={onCancel}
          className="flex items-center gap-2 px-5 py-3 rounded-2xl bg-white/10 hover:bg-white/20 active:scale-95 text-white font-medium text-lg backdrop-blur-md transition"
        >
          <ArrowLeft className="w-6 h-6" />
          <span>Quay lại</span>
        </button>

        <div className="text-center">
          <h2 className="text-3xl font-extrabold text-white tracking-wide">CHỌN BỐ CỤC & KHUNG HÌNH</h2>
          <p className="text-sm text-gray-400 mt-1">Chọn kích thước in ảnh và phong cách bạn yêu thích</p>
        </div>

        <div className="w-24"></div> {/* Spacer balance */}
      </div>

      {/* Main Options Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-12 max-w-5xl mx-auto w-full z-10 my-auto">
        {/* Strip 2x6 Option */}
        <div
          onClick={() => setSelectedType('strip2x6')}
          className={`cursor-pointer rounded-3xl p-8 border-2 transition-all duration-200 backdrop-blur-lg flex flex-col items-center justify-between ${
            selectedType === 'strip2x6'
              ? 'border-rose-500 bg-rose-500/10 shadow-[0_0_30px_rgba(244,63,94,0.3)] scale-[1.02]'
              : 'border-white/10 bg-white/5 hover:border-white/30'
          }`}
        >
          <div className="w-full flex justify-between items-center mb-6">
            <span className="text-xs font-bold uppercase tracking-wider text-rose-400 bg-rose-500/20 px-3 py-1 rounded-full">
              Phổ Biến Nhất
            </span>
            <Columns className="w-7 h-7 text-rose-400" />
          </div>

          {/* Graphical Representation of 2x6 Strip */}
          <div className="flex gap-4 p-4 rounded-xl bg-black/40 border border-white/10 my-4 shadow-inner">
            <div className="w-16 h-48 rounded bg-white/20 flex flex-col justify-around p-1.5 gap-1.5">
              <div className="w-full h-9 rounded-sm bg-rose-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-purple-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-indigo-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-sky-500/40"></div>
            </div>
            <div className="w-16 h-48 rounded bg-white/20 flex flex-col justify-around p-1.5 gap-1.5">
              <div className="w-full h-9 rounded-sm bg-rose-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-purple-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-indigo-500/40"></div>
              <div className="w-full h-9 rounded-sm bg-sky-500/40"></div>
            </div>
          </div>

          <div className="text-center mt-4">
            <h3 className="text-2xl font-bold text-white mb-1">Dải Kẹp 2x6 (Cặp 2 dải)</h3>
            <p className="text-gray-400 text-sm">In thành 2 dải ảnh thon dài cổ điển, chia sẻ ngay cho bạn bè</p>
          </div>
        </div>

        {/* Postcard 4x6 Option */}
        <div
          onClick={() => setSelectedType('postcard4x6')}
          className={`cursor-pointer rounded-3xl p-8 border-2 transition-all duration-200 backdrop-blur-lg flex flex-col items-center justify-between ${
            selectedType === 'postcard4x6'
              ? 'border-purple-500 bg-purple-500/10 shadow-[0_0_30px_rgba(168,85,247,0.3)] scale-[1.02]'
              : 'border-white/10 bg-white/5 hover:border-white/30'
          }`}
        >
          <div className="w-full flex justify-between items-center mb-6">
            <span className="text-xs font-bold uppercase tracking-wider text-purple-400 bg-purple-500/20 px-3 py-1 rounded-full">
              Khung Lớn Bưu Thiếp
            </span>
            <Square className="w-7 h-7 text-purple-400" />
          </div>

          {/* Graphical Representation of 4x6 Postcard */}
          <div className="p-4 rounded-xl bg-black/40 border border-white/10 my-4 shadow-inner">
            <div className="w-48 h-48 rounded bg-white/20 grid grid-cols-2 gap-2 p-3">
              <div className="rounded bg-purple-500/40"></div>
              <div className="rounded bg-rose-500/40"></div>
              <div className="rounded bg-indigo-500/40"></div>
              <div className="rounded bg-emerald-500/40"></div>
            </div>
          </div>

          <div className="text-center mt-4">
            <h3 className="text-2xl font-bold text-white mb-1">Bưu Thiếp 4x6 (1 Bản Rộng)</h3>
            <p className="text-gray-400 text-sm">Khổ ảnh ngang $10 \times 15$ cm trọn vẹn, hiển thị 4 bức ảnh lớn rực rỡ</p>
          </div>
        </div>
      </div>

      {/* Frame Theme Selection Bar */}
      <div className="max-w-4xl mx-auto w-full z-10 flex flex-col items-center gap-3">
        <div className="flex items-center gap-2 text-sm text-gray-300 font-medium">
          <Palette className="w-4 h-4 text-rose-400" />
          <span>Màu nền khung ảnh:</span>
        </div>
        
        <div className="flex gap-4">
          {THEMES.map((theme) => (
            <button
              key={theme.id}
              onClick={() => setSelectedTheme(theme)}
              className={`flex items-center gap-3 px-5 py-2.5 rounded-full border-2 transition active:scale-95 ${
                selectedTheme.id === theme.id
                  ? 'border-rose-400 bg-white/10 shadow-lg'
                  : 'border-white/10 bg-black/30 hover:border-white/30'
              }`}
            >
              <span
                className="w-5 h-5 rounded-full border border-black/20"
                style={{ backgroundColor: theme.bg }}
              ></span>
              <span className="text-sm font-medium text-white">{theme.name}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Next Step Confirmation Action */}
      <div className="max-w-md mx-auto w-full z-10 mt-6">
        <button
          onClick={handleConfirm}
          className="w-full py-5 rounded-2xl bg-gradient-to-r from-rose-500 via-purple-600 to-indigo-600 text-white font-bold text-xl tracking-wider shadow-xl flex items-center justify-center gap-3 active:scale-95 transition"
        >
          <span>TIẾP TỤC CHỤP ẢNH</span>
          <ArrowRight className="w-6 h-6" />
        </button>
      </div>
    </div>
  );
};
