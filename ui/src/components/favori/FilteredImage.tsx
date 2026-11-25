import React, { useEffect, useState } from 'react';
import type { FilterOption } from '../../services/backendApi';
import { applyLutToImage } from '../../services/lut';

type FilteredImageProps = Omit<React.ImgHTMLAttributes<HTMLImageElement>, 'src'> & {
  src: string;
  filter?: FilterOption;
};

/**
 * Ảnh đã áp bộ lọc đang chọn: bộ lọc màu dùng CSS filter, bộ lọc LUT (.cube) thì tính trên canvas.
 * Trong lúc tính LUT vẫn hiện ảnh gốc để không nhấp nháy trống.
 */
export const FilteredImage: React.FC<FilteredImageProps> = ({ src, filter, style, ...rest }) => {
  const lutUrl = filter?.lutUrl;
  const intensity = filter?.lutIntensity ?? 1;
  const [lutSrc, setLutSrc] = useState<string | null>(null);

  useEffect(() => {
    if (!lutUrl) {
      setLutSrc(null);
      return;
    }
    let active = true;
    applyLutToImage(src, lutUrl, intensity)
      .then((url) => active && setLutSrc(url))
      .catch((err) => {
        console.warn('[FilteredImage] Không áp được LUT:', err);
        if (active) setLutSrc(null);
      });
    return () => {
      active = false;
    };
  }, [src, lutUrl, intensity]);

  const css = lutUrl ? 'none' : filter?.cssFilter || 'none';
  return <img {...rest} src={lutUrl && lutSrc ? lutSrc : src} style={{ ...style, filter: css }} />;
};
