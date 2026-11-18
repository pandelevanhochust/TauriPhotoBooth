import React, { useState } from 'react';
import { ImageUp, Loader2, Scissors, FileImage } from 'lucide-react';
import { printerApi, type PickedImagePrintResult } from '../../services/printer';

/** Test in: chọn một ảnh có sẵn trên máy, app tự nhận khổ 2x6 / 4x6 theo kích thước ảnh rồi in ngay. */
export const PrintImageTestPanel: React.FC = () => {
  const [printing, setPrinting] = useState(false);
  const [result, setResult] = useState<PickedImagePrintResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handlePick = async () => {
    setPrinting(true);
    setError(null);
    try {
      const res = await printerApi.printPickedImage();
      // Đóng hộp thoại không chọn gì: giữ nguyên kết quả lần trước
      if (!res?.cancelled) setResult(res);
    } catch (e) {
      setResult(null);
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setPrinting(false);
    }
  };

  return (
    <div className="p-4 rounded-2xl bg-white/5 border border-white/10 space-y-4">
      <div>
        <div className="font-semibold text-sm flex items-center gap-2">
          <FileImage className="w-5 h-5 text-rose-400" />
          Test in ảnh từ máy
        </div>
        <p className="text-xs text-gray-400 mt-1 leading-relaxed">
          Chọn một ảnh JPG / PNG có sẵn trên máy, app sẽ tự in ngay. Khổ giấy nhận theo kích thước ảnh:
        </p>
        <ul className="text-xs text-gray-400 mt-1 space-y-0.5 list-disc pl-5">
          <li>
            Ảnh một dải tỉ lệ 1:3 (vd. 1200x3600): <b className="text-gray-200">2x6</b>, in hai dải trên tờ 4x6 rồi cắt đôi.
          </li>
          <li>
            Ảnh khác (vd. 2400x3600, tỉ lệ 2:3): <b className="text-gray-200">4x6</b>, không cắt.
          </li>
        </ul>
      </div>

      <button
        onClick={handlePick}
        disabled={printing}
        className="px-5 py-3 rounded-xl bg-rose-500 hover:bg-rose-600 text-sm font-bold transition active:scale-95 flex items-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed"
      >
        {printing ? <Loader2 className="w-4 h-4 animate-spin" /> : <ImageUp className="w-4 h-4" />}
        {printing ? 'Đang chờ chọn ảnh / gửi lệnh in...' : 'Chọn ảnh và in'}
      </button>

      {result && (
        <div className="text-xs rounded-xl px-4 py-3 border text-emerald-300 bg-emerald-500/10 border-emerald-500/30 space-y-1">
          <div className="font-semibold flex items-center gap-2">
            {result.message || 'Đã gửi lệnh in'}
            {result.kind === '2x6' && (
              <span className="inline-flex items-center gap-1 text-[10px] px-2 py-0.5 rounded-full bg-white/10 text-gray-200">
                <Scissors className="w-3 h-3" /> cắt đôi
              </span>
            )}
          </div>
          <div className="text-emerald-200/80">
            {result.fileName} · {result.width}x{result.height} px · nhận là khổ <b>{result.kind}</b>
            {typeof result.sheets === 'number' ? ` · ${result.sheets} tờ` : ''}
            {typeof result.remainingPaper === 'number' ? ` · giấy còn ${result.remainingPaper} tờ` : ''}
          </div>
        </div>
      )}

      {error && (
        <div className="text-xs rounded-xl px-4 py-3 border text-rose-300 bg-rose-500/10 border-rose-500/30 whitespace-pre-line">
          In thất bại: {error}
        </div>
      )}
    </div>
  );
};
