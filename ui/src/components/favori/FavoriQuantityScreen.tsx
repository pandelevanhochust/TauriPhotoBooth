import React, { useState } from 'react';
import { FavoriLayout } from './FavoriLayout';
import { Delete } from 'lucide-react';

interface FavoriQuantityScreenProps {
  quantity: number;
  onQuantityChange: (qty: number) => void;
  pricePerUnit: number;
  isFreeMode?: boolean;
  onNext: () => void;
  onBack: () => void;
}

export const FavoriQuantityScreen: React.FC<FavoriQuantityScreenProps> = ({
  quantity,
  onQuantityChange,
  pricePerUnit = 50000,
  isFreeMode = true,
  onNext,
  onBack,
}) => {
  const [promoCode, setPromoCode] = useState<string>('');
  const [discountApplied, setDiscountApplied] = useState<boolean>(false);
  const [discountMessage, setDiscountMessage] = useState<string>('');

  const keysRow1 = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0'];
  const keysRow2 = ['Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P'];
  const keysRow3 = ['A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L'];
  const keysRow4 = ['Z', 'X', 'C', 'V', 'B', 'N', 'M', '-'];

  const handleKeyPress = (char: string) => {
    if (promoCode.length < 12) {
      setPromoCode((prev) => prev + char);
    }
  };

  const handleBackspace = () => {
    setPromoCode((prev) => prev.slice(0, -1));
  };

  const handleApplyPromo = () => {
    if (!promoCode.trim()) return;
    if (promoCode.toUpperCase() === 'FAVORI' || promoCode.toUpperCase() === 'VIP' || promoCode.toUpperCase() === 'HIHI') {
      setDiscountApplied(true);
      setDiscountMessage('Áp dụng mã giảm giá thành công!');
    } else {
      setDiscountMessage('Mã giảm giá không hợp lệ.');
    }
  };

  const calculateTotal = () => {
    if (isFreeMode) return 'MIỄN PHÍ (SỰ KIỆN)';
    let total = quantity * pricePerUnit;
    if (discountApplied) total = Math.max(0, total - 20000);
    return `${total.toLocaleString('vi-VN')} đ`;
  };

  return (
    <FavoriLayout
      stepNumber={3}
      stepTitle="Chọn số lượng in và nhập mã giảm giá"
      hideHeader={false}
      onBack={undefined} // Back & Next are placed directly inside the right panel per 4.png
      onNext={undefined}
    >
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 w-full max-w-5xl items-center py-2">
        {/* Left Side: Promo Code & Touch Keyboard (7 cols) */}
        <div className="lg:col-span-7 flex flex-col space-y-4">
          <label className="text-lg font-serif-vintage italic font-bold text-[#1f1610]">
            Nhập mã giảm giá :
          </label>

          {/* Promo Input & Apply Button */}
          <div className="flex items-center gap-3">
            <div className="flex-1 h-12 bg-white/90 border-2 border-[#bfa282] rounded-xl px-4 flex items-center text-xl font-mono tracking-wider text-[#1f1610] shadow-inner">
              {promoCode || <span className="text-neutral-400 font-sans text-base">Nhập mã ưu đãi tại đây</span>}
            </div>

            <button
              onClick={handleApplyPromo}
              className="btn-favori-primary px-6 h-12 text-base font-semibold min-w-[110px] cursor-pointer"
            >
              Áp dụng
            </button>
          </div>

          {discountMessage && (
            <p className={`text-sm font-medium ${discountApplied ? 'text-emerald-700' : 'text-red-600'}`}>
              {discountMessage}
            </p>
          )}

          {/* On-screen touch keyboard matching 4.png */}
          <div className="bg-[#ede4d8]/70 border border-[#d6c4b2] rounded-2xl p-4 shadow-sm flex flex-col items-center space-y-2 mt-2">
            {/* Row 1: Numbers + Backspace */}
            <div className="flex justify-center gap-1.5 w-full">
              {keysRow1.map((k) => (
                <button
                  key={k}
                  onClick={() => handleKeyPress(k)}
                  className="flex-1 h-11 bg-white hover:bg-neutral-50 active:bg-neutral-200 text-[#1f1610] font-semibold text-lg rounded-lg shadow-sm border border-[#e0d3c5] transition-all cursor-pointer"
                >
                  {k}
                </button>
              ))}
              <button
                onClick={handleBackspace}
                className="w-14 h-11 bg-[#e8d5c4] hover:bg-[#dec4af] active:bg-[#cbb099] text-[#7a0000] flex items-center justify-center rounded-lg shadow-sm border border-[#d1baa5] transition-all cursor-pointer"
                title="Backspace"
              >
                <Delete className="w-5 h-5" />
              </button>
            </div>

            {/* Row 2: Q - P */}
            <div className="flex justify-center gap-1.5 w-full">
              {keysRow2.map((k) => (
                <button
                  key={k}
                  onClick={() => handleKeyPress(k)}
                  className="flex-1 h-11 bg-white hover:bg-neutral-50 active:bg-neutral-200 text-[#1f1610] font-semibold text-lg rounded-lg shadow-sm border border-[#e0d3c5] transition-all cursor-pointer"
                >
                  {k}
                </button>
              ))}
            </div>

            {/* Row 3: A - L */}
            <div className="flex justify-center gap-1.5 w-11/12">
              {keysRow3.map((k) => (
                <button
                  key={k}
                  onClick={() => handleKeyPress(k)}
                  className="flex-1 h-11 bg-white hover:bg-neutral-50 active:bg-neutral-200 text-[#1f1610] font-semibold text-lg rounded-lg shadow-sm border border-[#e0d3c5] transition-all cursor-pointer"
                >
                  {k}
                </button>
              ))}
            </div>

            {/* Row 4: Z - M, - */}
            <div className="flex justify-center gap-1.5 w-5/6">
              {keysRow4.map((k) => (
                <button
                  key={k}
                  onClick={() => handleKeyPress(k)}
                  className="flex-1 h-11 bg-white hover:bg-neutral-50 active:bg-neutral-200 text-[#1f1610] font-semibold text-lg rounded-lg shadow-sm border border-[#e0d3c5] transition-all cursor-pointer"
                >
                  {k}
                </button>
              ))}
            </div>

            {/* Row 5: SPACE and APPLY */}
            <div className="flex justify-center gap-3 w-3/4 pt-1">
              <button
                onClick={() => handleKeyPress(' ')}
                className="flex-2 h-11 bg-white hover:bg-neutral-50 active:bg-neutral-200 text-[#1f1610] font-semibold text-sm rounded-lg shadow-sm border border-[#e0d3c5] tracking-wider transition-all cursor-pointer"
              >
                SPACE
              </button>
              <button
                onClick={handleApplyPromo}
                className="flex-1 h-11 bg-[#19533a] hover:bg-[#12422e] text-white font-semibold text-sm rounded-lg shadow-sm tracking-wider flex items-center justify-center gap-1 transition-all cursor-pointer"
              >
                APPLY ✓
              </button>
            </div>
          </div>
        </div>

        {/* Right Side: Quantity Stepper & Price Card (5 cols) matching 4.png */}
        <div className="lg:col-span-5 flex justify-center">
          <div className="favori-card w-full max-w-sm p-8 flex flex-col items-center justify-between border-2 border-[#bfa282]/80 shadow-xl space-y-6">
            {/* Quantity Stepper */}
            <div className="w-full flex flex-col items-center space-y-3">
              <span className="font-serif-vintage italic text-lg text-[#1f1610] font-bold">
                Chọn số lượng bản in :
              </span>
              <div className="flex items-center justify-center gap-2 w-full">
                <button
                  onClick={() => onQuantityChange(Math.max(2, quantity - 2))}
                  className="w-12 h-12 rounded-lg border-2 border-[#bfa282] bg-white hover:bg-neutral-100 active:scale-95 text-2xl font-bold text-[#1f1610] flex items-center justify-center cursor-pointer shadow-sm"
                >
                  -
                </button>
                <div className="flex-1 h-12 bg-neutral-100 border-2 border-[#bfa282] rounded-lg flex items-center justify-center text-2xl font-bold font-mono text-[#7a0000]">
                  {quantity}
                </div>
                <button
                  onClick={() => onQuantityChange(Math.min(10, quantity + 2))}
                  className="w-12 h-12 rounded-lg border-2 border-[#bfa282] bg-white hover:bg-neutral-100 active:scale-95 text-2xl font-bold text-[#1f1610] flex items-center justify-center cursor-pointer shadow-sm"
                >
                  +
                </button>
              </div>
            </div>

            {/* Total Price Display */}
            <div className="w-full flex flex-col items-center space-y-3">
              <span className="font-serif-vintage italic text-lg text-[#1f1610] font-bold">
                Tổng tiền thanh toán :
              </span>
              <div className="w-full h-12 bg-neutral-100 border-2 border-[#bfa282] rounded-lg flex items-center justify-center text-xl font-bold text-[#7a0000]">
                {calculateTotal()}
              </div>
            </div>

            {/* Action Buttons: Tiếp tục & Quay về */}
            <div className="w-full flex flex-col items-center space-y-3 pt-4">
              <button
                onClick={onNext}
                className="btn-favori-primary w-full py-3.5 text-lg font-semibold shadow-lg hover:brightness-105 active:scale-95 cursor-pointer"
              >
                Tiếp tục
              </button>
              <button
                onClick={onBack}
                className="btn-favori-primary w-full py-3 text-base font-semibold shadow-md hover:brightness-105 active:scale-95 cursor-pointer opacity-90"
              >
                Quay về
              </button>
            </div>
          </div>
        </div>
      </div>
    </FavoriLayout>
  );
};
