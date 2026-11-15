import { useEffect, useRef, useState } from 'react';

/**
 * Đếm ngược theo giây. Gọi `onExpire` đúng một lần khi về 0.
 * `paused` tạm dừng đồng hồ mà không đặt lại.
 */
export function useCountdown(
  seconds: number,
  onExpire: () => void,
  options: { paused?: boolean } = {},
): number {
  const { paused = false } = options;
  const [timeLeft, setTimeLeft] = useState<number>(seconds);
  const onExpireRef = useRef(onExpire);
  const firedRef = useRef(false);

  useEffect(() => {
    onExpireRef.current = onExpire;
  });

  useEffect(() => {
    if (paused || timeLeft <= 0) return;
    const timer = setTimeout(() => setTimeLeft((t) => t - 1), 1000);
    return () => clearTimeout(timer);
  }, [timeLeft, paused]);

  useEffect(() => {
    if (timeLeft <= 0 && !firedRef.current) {
      firedRef.current = true;
      onExpireRef.current();
    }
  }, [timeLeft]);

  return Math.max(0, timeLeft);
}
