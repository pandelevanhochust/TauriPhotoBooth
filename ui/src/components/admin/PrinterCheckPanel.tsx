import React, { useEffect, useState } from 'react';
import { Printer, RefreshCw, Loader2, FileCheck } from 'lucide-react';
import { printerApi, type PrintCalibration, type PrinterStatus } from '../../services/printer';

/** Kiểm tra máy in: trạng thái hàng đợi in của Windows, chọn máy in, in thử. */
export const PrinterCheckPanel: React.FC = () => {
  const [status, setStatus] = useState<PrinterStatus | null>(null);
  const [checking, setChecking] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);
  const [calibration, setCalibration] = useState<PrintCalibration | null>(null);
  const [savedNote, setSavedNote] = useState('');

  // Nạp giá trị căn chỉnh từ host một lần; sau đó giữ giá trị người dùng đang gõ
  useEffect(() => {
    if (status && calibration === null) setCalibration(status.calibration);
  }, [status, calibration]);

  const handleSaveCalibration = async () => {
    if (!calibration) return;
    const next = await printerApi.calibrate(calibration);
    setStatus(next);
    setCalibration(next.calibration);
    setSavedNote('Đã lưu. Áp dụng từ lần in tiếp theo.');
  };

  const calibrationField = (key: keyof PrintCalibration, label: string, step: number, unit: string) => (
    <label className="flex flex-col gap-1 text-[11px] text-gray-400">
      {label}
      <span className="flex items-center gap-1">
        <input
          type="number"
          step={step}
          value={calibration ? calibration[key] : ''}
          onChange={(e) => {
            const value = Number(e.target.value);
            setSavedNote('');
            if (calibration && Number.isFinite(value)) setCalibration({ ...calibration, [key]: value });
          }}
          className="w-20 bg-black/50 border border-white/20 rounded-lg px-2 py-1 text-xs text-white"
        />
        {unit}
      </span>
    </label>
  );

  useEffect(() => {
    let active = true;
    printerApi.getStatus().then((s) => active && setStatus(s));
    return () => {
      active = false;
    };
  }, []);

  const handleCheck = async () => {
    setChecking(true);
    setResult(null);
    try {
      setStatus(await printerApi.getStatus());
    } finally {
      setChecking(false);
    }
  };

  const handleSelect = async (name: string) => {
    setResult(null);
    setStatus(await printerApi.select(name));
  };

  const handleTestPrint = async () => {
    setPrinting(true);
    setResult(null);
    try {
      const res = await printerApi.testPrint();
      setResult({ ok: true, text: res?.message || 'Đã gửi lệnh in thử' });
      setStatus(await printerApi.getStatus());
    } catch (e) {
      setResult({ ok: false, text: e instanceof Error ? e.message : String(e) });
    } finally {
      setPrinting(false);
    }
  };

  const ready = !!status?.ready;
  const driverMissing = !!status && !status.selected && status.usbWithoutDriver.length > 0;
  const dotClass = status === null ? 'bg-gray-500' : ready ? 'bg-emerald-400' : driverMissing ? 'bg-amber-400' : 'bg-rose-500';
  const selected = status?.printers.find((p) => p.name === status.selected);

  const actionBtn =
    'px-4 py-2 rounded-xl text-xs font-bold transition active:scale-95 flex items-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed';

  return (
    <div className="p-4 rounded-2xl bg-white/5 border border-white/10 space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-3 min-w-0">
          <Printer className="w-5 h-5 text-rose-400 mt-0.5 flex-shrink-0" />
          <div className="min-w-0">
            <div className="font-semibold text-sm flex items-center gap-2">
              <span className={`inline-block w-2.5 h-2.5 rounded-full ${dotClass}`} />
              {status === null
                ? 'Đang kiểm tra máy in...'
                : status.selected
                  ? `Máy in: ${status.selected}`
                  : driverMissing
                    ? 'Máy in đã cắm nhưng chưa có driver'
                    : 'Chưa có máy in ảnh'}
            </div>
            {status && (
              <div className="text-xs text-gray-400 mt-1 space-y-0.5">
                <div className="text-gray-300 whitespace-pre-line">{status.message}</div>
                {selected && (
                  <div>
                    Driver: {selected.driver || '?'} · Cổng: {selected.port || '?'}
                    {selected.jobCount > 0 ? ` · Đang chờ in: ${selected.jobCount}` : ''}
                  </div>
                )}
                <div>Giấy còn lại (bộ đếm của app): {status.paperCount} tờ</div>
              </div>
            )}
          </div>
        </div>
        <button
          onClick={handleCheck}
          disabled={checking}
          className={`${actionBtn} bg-white/10 hover:bg-white/20 flex-shrink-0`}
        >
          {checking ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCw className="w-4 h-4" />}
          Kiểm tra lại
        </button>
      </div>

      {status && status.printers.length > 0 && (
        <div className="flex items-center gap-3">
          <label className="text-xs text-gray-400 flex-shrink-0">Máy in dùng cho booth</label>
          <select
            value={status.configured}
            onChange={(e) => handleSelect(e.target.value)}
            className="flex-1 min-w-0 bg-black/50 border border-white/20 rounded-xl px-3 py-2 text-xs text-white"
          >
            <option value="">Tự chọn máy in ảnh (DNP, Citizen, ...)</option>
            {status.printers.map((p) => (
              <option key={p.name} value={p.name}>
                {p.name}
                {p.isVirtual ? ' (máy in ảo)' : ''} — {p.statusText}
              </option>
            ))}
          </select>
          <button
            onClick={handleTestPrint}
            disabled={printing || !status.selected}
            className={`${actionBtn} bg-rose-500 hover:bg-rose-600 flex-shrink-0`}
          >
            {printing ? <Loader2 className="w-4 h-4 animate-spin" /> : <FileCheck className="w-4 h-4" />}
            In thử
          </button>
        </div>
      )}

      {status && status.paperSizes.length > 0 && (
        <div className="text-[11px] text-gray-500 leading-relaxed">
          Khổ giấy của driver: {status.paperSizes.join(' · ')}
        </div>
      )}

      {calibration && (
        <div className="space-y-2 pt-3 border-t border-white/10">
          <div className="text-xs font-semibold text-gray-300">Căn chỉnh bản in</div>
          <div className="flex flex-wrap items-end gap-4">
            <span className="w-16 text-[11px] text-gray-300">Dải 2x6</span>
            {calibrationField('stripScalePercent', 'Tỉ lệ', 0.5, '%')}
            {calibrationField('stripOffsetX', 'Dịch ngang', 1, '')}
            {calibrationField('stripOffsetY', 'Dịch dọc', 1, '')}
            {calibrationField('stripGapMm', 'Khe giữa hai dải', 0.1, 'mm')}
          </div>
          <div className="flex flex-wrap items-end gap-4">
            <span className="w-16 text-[11px] text-gray-300">Ảnh 4x6</span>
            {calibrationField('scalePercent', 'Tỉ lệ', 0.5, '%')}
            {calibrationField('offsetX', 'Dịch ngang', 1, '')}
            {calibrationField('offsetY', 'Dịch dọc', 1, '')}
            <button onClick={handleSaveCalibration} className={`${actionBtn} bg-white/10 hover:bg-white/20`}>
              Lưu căn chỉnh
            </button>
          </div>
          <div className="text-[11px] text-gray-500 leading-relaxed">
            Cùng đơn vị với app FavoriCurrent: dịch tính bằng 1/100 inch, số dương là sang phải / xuống dưới.
            {savedNote ? ` ${savedNote}` : ''}
          </div>
        </div>
      )}

      {result && (
        <div
          className={`text-xs rounded-xl px-3 py-2 border whitespace-pre-line ${
            result.ok
              ? 'text-emerald-300 bg-emerald-500/10 border-emerald-500/30'
              : 'text-rose-300 bg-rose-500/10 border-rose-500/30'
          }`}
        >
          {result.ok ? result.text : `In thử thất bại: ${result.text}`}
        </div>
      )}
    </div>
  );
};
