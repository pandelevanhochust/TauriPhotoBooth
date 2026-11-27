import React, { useEffect, useState } from 'react';
import type { FilterOption } from '../../services/backendApi';
import { applyLutToImage } from '../../services/lut';
import { smoothSkinImage, type SkinLevel } from '../../services/skin';

type FilteredImageProps = Omit<React.ImgHTMLAttributes<HTMLImageElement>, 'src'> & {
  src: string;
  filter?: FilterOption;
  /** Làm mịn da (thử nghiệm), áp trước bộ lọc màu. */
  skin?: SkinLevel;
};

/** Kết quả xử lý kèm khoá của đầu vào: khi ảnh / bộ lọc đổi, kết quả cũ không được hiện nhầm cho ảnh mới. */
interface Processed {
  key: string;
  url: string;
}

/**
 * Ảnh đã áp làm mịn da (nếu bật) và bộ lọc đang chọn: bộ lọc màu dùng CSS filter, bộ lọc LUT (.cube) thì tính trên GPU.
 * Trong lúc xử lý vẫn hiện ảnh trước đó / ảnh gốc để không nhấp nháy trống.
 */
export const FilteredImage: React.FC<FilteredImageProps> = ({ src, filter, skin = 'off', style, ...rest }) => {
  const lutUrl = filter?.lutUrl;
  const intensity = filter?.lutIntensity ?? 1;
  const [smooth, setSmooth] = useState<Processed | null>(null);
  const [lut, setLut] = useState<Processed | null>(null);

  // 1. Làm mịn da
  const smoothKey = `${skin}|${src}`;
  useEffect(() => {
    if (skin === 'off') return;
    let active = true;
    smoothSkinImage(src, skin).then((url) => active && setSmooth({ key: smoothKey, url }));
    return () => {
      active = false;
    };
  }, [src, skin, smoothKey]);
  const base = skin !== 'off' && smooth?.key === smoothKey ? smooth.url : src;

  // 2. Bộ lọc LUT trên ảnh đã làm mịn
  const lutKey = `${lutUrl}|${intensity}|${base}`;
  useEffect(() => {
    if (!lutUrl) return;
    let active = true;
    applyLutToImage(base, lutUrl, intensity)
      .then((url) => active && setLut({ key: lutKey, url }))
      .catch((err) => console.warn('[FilteredImage] Không áp được LUT:', err));
    return () => {
      active = false;
    };
  }, [base, lutUrl, intensity, lutKey]);

  const shown = lutUrl && lut?.key === lutKey ? lut.url : base;
  const css = lutUrl ? 'none' : filter?.cssFilter || 'none';
  return <img {...rest} src={shown} style={{ ...style, filter: css }} />;
};
