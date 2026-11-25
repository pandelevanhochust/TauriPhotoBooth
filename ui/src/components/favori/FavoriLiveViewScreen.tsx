import React, { useState, useEffect, useRef, useCallback } from 'react';
import { Camera, RotateCcw } from 'lucide-react';
import { ipc } from '../../services/ipc';
import { cameraApi } from '../../services/camera';
import { useCountdown } from '../../hooks/useCountdown';

// Tỉ lệ khung ngắm: webcam 16:9, máy ảnh Canon (cảm biến APS-C) 3:2
const WEBCAM_ASPECT = 16 / 9;
const DSLR_ASPECT = 3 / 2;

type CameraSource = 'detecting' | 'dslr' | 'webcam';

// Video bước chụp: khung webcam gửi cho host mỗi 83ms = 12 khung/giây (máy ảnh SDK thì host tự lấy khung live view)
/** Video đầy đủ 15 khung/giây (host đặt khung theo thời điểm thật). */
const TIMELAPSE_SAMPLE_MS = 66;
const TIMELAPSE_FRAME_WIDTH = 1280;

// Rời bước chụp: chờ tối đa bấy lâu cho các ảnh còn đang tải từ máy ảnh về
const PENDING_SHOTS_TIMEOUT_MS = 12000;
// Thông báo "chụp lại" hiện bấy lâu trong khung ngắm
const RETAKE_NOTICE_MS = 5000;

// Ảnh vừa chụp hiện trong khung ngắm tối thiểu bấy lâu (máy ảnh chưa truyền xong thì hiện tới khi live view chạy lại)
const REVIEW_MS = 1000;
// Ảnh xem lại giữ tối đa bấy lâu dù máy ảnh chưa truyền xong / live view chưa chạy lại (chế độ tự động cũng đếm tiếp)
const REVIEW_WAIT_MAX_MS = 6000;
// Live view coi như đã chạy lại khi nhận đủ số khung mới này sau khi ảnh truyền xong (~0,1 giây ở 30 khung/giây)
const LIVE_RESUME_FRAMES = 3;

/**
 * Phím do nút bấm rời gửi tới (nút USB, bút trình chiếu, remote Bluetooth chụp ảnh đều giả lập bàn phím).
 * Chế độ chụp bằng điều khiển từ xa: bấm một trong các phím này là chụp một tấm.
 */
const REMOTE_TRIGGER_KEYS = new Set([
  ' ',
  'Enter',
  'PageDown',
  'PageUp',
  'ArrowRight',
  'ArrowLeft',
  'ArrowUp',
  'ArrowDown',
  'b',
  'B',
  '.',
  'AudioVolumeUp',
  'AudioVolumeDown',
  'MediaPlayPause',
]);

interface FavoriLiveViewScreenProps {
  totalShots?: number;
  mode?: 'remote' | 'autoCountdown';
  /** Số giây đếm ngược trước mỗi tấm ở chế độ tự động. */
  countdownSec?: number;
  /** Tổng thời gian cho chế độ nút bấm (giây). */
  remoteTimeoutSec?: number;
  /** Ghi video timelapse trong lúc chụp. */
  timelapseEnabled?: boolean;
  /** Tỉ lệ rộng / cao của ô ảnh trong frame; có giá trị thì vẽ đường kẻ và phủ mờ phần ảnh sẽ bị cắt khi in. */
  guideAspect?: number;
  /** photos: ảnh để hiển thị (data URL); filePaths: ảnh gốc trên máy (cùng thứ tự, có thể rỗng). */
  onAllShotsCaptured: (photos: string[], filePaths: string[]) => void;
  onCancel: () => void;
}

function playShutterSound() {
  try {
    const AudioCtor =
      window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    const audioCtx = new AudioCtor();
    const osc = audioCtx.createOscillator();
    const gain = audioCtx.createGain();
    osc.connect(gain);
    gain.connect(audioCtx.destination);
    osc.frequency.setValueAtTime(800, audioCtx.currentTime);
    gain.gain.setValueAtTime(0.3, audioCtx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.1);
    osc.start();
    osc.stop(audioCtx.currentTime + 0.1);
    setTimeout(() => {
      audioCtx.close().catch(() => {});
    }, 200);
  } catch {
    // Audio context might be restricted
  }
}

function placeholderShot(shotIndex: number): string {
  const c = document.createElement('canvas');
  c.width = 1200;
  c.height = 800;
  const ctx = c.getContext('2d');
  if (!ctx) return '';
  ctx.fillStyle = shotIndex % 2 === 0 ? '#f5ece2' : '#e8dcce';
  ctx.fillRect(0, 0, 1200, 800);
  ctx.fillStyle = '#7a0000';
  ctx.font = 'bold 44px serif';
  ctx.textAlign = 'center';
  ctx.fillText(`Favori Photo Shot #${shotIndex}`, 600, 400);
  return c.toDataURL('image/jpeg', 0.9);
}

export const FavoriLiveViewScreen: React.FC<FavoriLiveViewScreenProps> = ({
  totalShots = 10,
  mode = 'autoCountdown',
  countdownSec = 10,
  remoteTimeoutSec = 180,
  timelapseEnabled = false,
  guideAspect,
  onAllShotsCaptured,
}) => {
  const isRemote = mode === 'remote';

  const [shotsTaken, setShotsTaken] = useState<number>(0);
  const [countdown, setCountdown] = useState<number | null>(isRemote ? null : countdownSec);
  const [isCapturing, setIsCapturing] = useState<boolean>(false);
  const [isFinished, setIsFinished] = useState<boolean>(false);
  const [isFlashing, setIsFlashing] = useState<boolean>(false);
  const [cameraReady, setCameraReady] = useState<boolean>(false);
  const [source, setSource] = useState<CameraSource>('detecting');
  // Thông báo tấm vừa chụp bị bỏ, khách phải chụp lại tấm đó
  const [captureError, setCaptureError] = useState<{ shot: number; reason: string } | null>(null);
  const [isSaving, setIsSaving] = useState<boolean>(false);

  const videoRef = useRef<HTMLVideoElement | null>(null);
  const liveImgRef = useRef<HTMLImageElement | null>(null);
  const photosRef = useRef<string[]>([]);
  const pathsRef = useRef<string[]>([]);
  // Mã tấm chụp (tăng dần, không dùng lại) song song với photosRef / pathsRef
  const seqsRef = useRef<number[]>([]);
  const nextSeqRef = useRef<number>(1);
  // Các tấm đã nhả màn trập nhưng ảnh còn đang tải từ máy ảnh về
  const pendingRef = useRef<Set<number>>(new Set());
  const pendingWaitersRef = useRef<Array<() => void>>([]);
  const sourceRef = useRef<CameraSource>('detecting');
  sourceRef.current = source;
  const liveFrameRef = useRef<string | null>(null);
  const inFlightRef = useRef<Promise<string[]> | null>(null);
  const finishedRef = useRef<boolean>(false);
  const errorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Ảnh vừa chụp đang xem lại trong khung ngắm (null = đang hiện live view)
  const [reviewPhoto, setReviewPhoto] = useState<string | null>(null);
  const reviewTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Mã tấm đang xem lại
  const reviewSeqRef = useRef<number | null>(null);
  // Việc chờ ảnh xem lại xong (chế độ tự động: bắt đầu đếm tấm sau)
  const afterReviewRef = useRef<(() => void) | null>(null);

  const runAfterReview = () => {
    const next = afterReviewRef.current;
    afterReviewRef.current = null;
    next?.();
  };

  const reviewSafetyRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Đã đủ thời gian xem tối thiểu (REVIEW_MS) chưa
  const reviewMinDoneRef = useRef(false);
  // Số khung live view mới nhận được kể từ khi ảnh của tấm đang xem lại truyền xong
  const freshFramesRef = useRef(0);
  // Thời điểm bắt đầu hiện ảnh xem lại (tấm cuối: giữ đủ REVIEW_MS rồi mới sang bước sau)
  const reviewShownAtRef = useRef(0);

  const clearReviewTimers = () => {
    if (reviewTimerRef.current) clearTimeout(reviewTimerRef.current);
    if (reviewSafetyRef.current) clearTimeout(reviewSafetyRef.current);
    reviewTimerRef.current = null;
    reviewSafetyRef.current = null;
  };

  /** Gỡ ảnh xem lại ngay, rồi chạy việc đang chờ (chế độ tự động: bắt đầu đếm tấm sau). */
  const hideReview = () => {
    clearReviewTimers();
    reviewSeqRef.current = null;
    reviewMinDoneRef.current = false;
    setReviewPhoto(null);
    runAfterReview();
  };

  /**
   * Gỡ ảnh xem lại khi: đã xem đủ REVIEW_MS, ảnh của tấm đó đã truyền xong, và live view đã có khung mới.
   * Máy ảnh không gửi khung live view trong lúc lấy nét / nhả màn trập / truyền ảnh (1–3 giây), nên gỡ sớm hơn sẽ lộ
   * khung hình đứng yên. Webcam không phải chờ.
   */
  const tryEndReview = () => {
    const seq = reviewSeqRef.current;
    // Đã chụp đủ: giữ ảnh tấm cuối tới khi sang bước sau, không quay lại live view
    if (seq === null || !reviewMinDoneRef.current || finishedRef.current) return;
    const waitingCamera =
      sourceRef.current === 'dslr' && (pendingRef.current.has(seq) || freshFramesRef.current < LIVE_RESUME_FRAMES);
    if (!waitingCamera) hideReview();
  };

  /** Hiện ảnh vừa chụp trong khung ngắm (xem tryEndReview để biết khi nào gỡ). */
  const showReview = (photo: string, seq: number) => {
    clearReviewTimers();
    reviewSeqRef.current = seq;
    reviewMinDoneRef.current = false;
    freshFramesRef.current = 0;
    reviewShownAtRef.current = Date.now();
    setReviewPhoto(photo);
    reviewTimerRef.current = setTimeout(() => {
      reviewMinDoneRef.current = true;
      tryEndReview();
    }, REVIEW_MS);
    // Máy ảnh không truyền xong / live view không chạy lại: vẫn gỡ để khách không bị kẹt
    reviewSafetyRef.current = setTimeout(() => {
      if (reviewSeqRef.current === seq && !finishedRef.current) hideReview();
    }, REVIEW_WAIT_MAX_MS);
  };

  // Có máy ảnh Canon thì dùng máy ảnh, không thì webcam; đổi theo khi cắm / rút máy giữa chừng
  useEffect(() => {
    let active = true;
    cameraApi.getStatus().then((s) => {
      if (active) setSource(s.connected ? 'dslr' : 'webcam');
    });
    const unlisten = cameraApi.onStatusChanged((s) => {
      setSource((cur) => (cur === 'detecting' ? cur : s.connected ? 'dslr' : 'webcam'));
    });
    return () => {
      active = false;
      unlisten();
      if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
      if (reviewTimerRef.current) clearTimeout(reviewTimerRef.current);
      if (reviewSafetyRef.current) clearTimeout(reviewSafetyRef.current);
    };
  }, []);

  // Báo host khách đang ở bước chụp: hàng đợi gửi ảnh lên server tạm dừng để không giật live view
  useEffect(() => {
    ipc.invoke('capture_step', { active: true }).catch(() => undefined);
    return () => {
      ipc.invoke('capture_step', { active: false }).catch(() => undefined);
    };
  }, []);

  // Ghi timelapse suốt bước chụp; rời màn hình thì host dừng và mã hoá MP4 ở chế độ nền
  useEffect(() => {
    if (!timelapseEnabled) return;
    ipc.invoke('timelapse_start').catch(() => undefined);
    return () => {
      ipc.invoke('timelapse_stop').catch(() => undefined);
    };
  }, [timelapseEnabled]);

  // Webcam: tự lấy khung gửi cho host (máy ảnh SDK thì host lấy thẳng từ live view)
  useEffect(() => {
    if (!timelapseEnabled || source !== 'webcam') return;
    const canvas = document.createElement('canvas');
    const timer = setInterval(() => {
      const video = videoRef.current;
      if (!video || video.videoWidth === 0 || finishedRef.current) return;
      const scale = Math.min(1, TIMELAPSE_FRAME_WIDTH / video.videoWidth);
      canvas.width = Math.round(video.videoWidth * scale);
      canvas.height = Math.round(video.videoHeight * scale);
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      ipc.invoke('timelapse_frame', { base64: canvas.toDataURL('image/jpeg', 0.7) }).catch(() => undefined);
    }, TIMELAPSE_SAMPLE_MS);
    return () => clearInterval(timer);
  }, [timelapseEnabled, source]);

  // Live view từ máy ảnh (host gửi khung JPEG)
  useEffect(() => {
    if (source !== 'dslr') return;
    setCameraReady(false);
    let gotFrame = false;
    const unlisten = ipc.listen<{ base64: string }>('camera_frame', (data: { base64: string }) => {
      if (data && data.base64) {
        const frame = `data:image/jpeg;base64,${data.base64}`;
        liveFrameRef.current = frame;
        // Gán thẳng vào thẻ ảnh: ~30 khung/giây mà không vẽ lại cả màn hình
        if (liveImgRef.current) liveImgRef.current.src = frame;
        // Đang xem lại một tấm: đếm khung mới sau khi ảnh tấm đó truyền xong, đủ thì gỡ ảnh xem lại
        const seq = reviewSeqRef.current;
        if (seq !== null && !pendingRef.current.has(seq)) {
          freshFramesRef.current += 1;
          if (freshFramesRef.current >= LIVE_RESUME_FRAMES) tryEndReview();
        }
        if (!gotFrame) {
          gotFrame = true;
          setCameraReady(true);
        }
      }
    });
    cameraApi.startLiveView();
    return () => {
      unlisten();
      cameraApi.stopLiveView();
      liveFrameRef.current = null;
    };
  }, [source]);

  const settlePending = (seq: number) => {
    pendingRef.current.delete(seq);
    // Ảnh của tấm đang xem lại vừa truyền xong: chỉ tính các khung live view từ lúc này
    if (reviewSeqRef.current === seq) freshFramesRef.current = 0;
    if (pendingRef.current.size === 0) {
      const waiters = pendingWaitersRef.current;
      pendingWaitersRef.current = [];
      waiters.forEach((resolve) => resolve());
    }
  };

  // Ảnh của máy ảnh tải về ở chế độ nền: thay ảnh tạm bằng ảnh thật, hoặc bỏ tấm lỗi
  useEffect(() => {
    const offReady = cameraApi.onShotReady((shot) => {
      const idx = seqsRef.current.indexOf(shot.shotIndex);
      if (idx >= 0) {
        const photos = [...photosRef.current];
        const paths = [...pathsRef.current];
        photos[idx] = shot.base64.startsWith('data:') ? shot.base64 : `data:image/jpeg;base64,${shot.base64}`;
        paths[idx] = shot.filePath || '';
        photosRef.current = photos;
        pathsRef.current = paths;
      }
      settlePending(shot.shotIndex);
    });
    const offFailed = cameraApi.onShotFailed((shot) => {
      const idx = seqsRef.current.indexOf(shot.shotIndex);
      if (idx >= 0 && !finishedRef.current) {
        // Chưa rời bước chụp: bỏ tấm lỗi để khách chụp bù
        photosRef.current = photosRef.current.filter((_, i) => i !== idx);
        pathsRef.current = pathsRef.current.filter((_, i) => i !== idx);
        seqsRef.current = seqsRef.current.filter((_, i) => i !== idx);
        setShotsTaken(photosRef.current.length);
        if (reviewSeqRef.current === shot.shotIndex) hideReview();
        else runAfterReview(); // không có ảnh để xem lại: đếm tấm sau luôn
        showCaptureError(shot.message);
      }
      settlePending(shot.shotIndex);
    });
    return () => {
      offReady();
      offFailed();
    };
  }, []);

  // Webcam của máy tính (khi không có máy ảnh Canon)
  useEffect(() => {
    if (source !== 'webcam') return;
    let stream: MediaStream | null = null;
    let isCancelled = false;

    async function initWebcam() {
      try {
        let s: MediaStream;
        try {
          s = await navigator.mediaDevices.getUserMedia({
            video: {
              width: { ideal: 1920, min: 640 },
              height: { ideal: 1080, min: 480 },
              facingMode: 'user',
            },
            audio: false,
          });
        } catch (strictErr) {
          console.warn('[FavoriLiveViewScreen] Strict constraints failed, falling back to basic video: true', strictErr);
          s = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        }

        if (isCancelled) {
          s.getTracks().forEach((t) => t.stop());
          return;
        }

        stream = s;
        if (videoRef.current) {
          videoRef.current.srcObject = s;
          try {
            await videoRef.current.play();
          } catch (playErr) {
            console.warn('[FavoriLiveViewScreen] Video play auto-play warning:', playErr);
          }
          setCameraReady(true);
        }
      } catch (err) {
        console.error('[FavoriLiveViewScreen] Webcam stream not accessible:', err);
        setCameraReady(true);
      }
    }

    initWebcam();

    return () => {
      isCancelled = true;
      if (stream) {
        stream.getTracks().forEach((track) => track.stop());
      }
    };
  }, [source]);

  const finish = useCallback(async () => {
    if (finishedRef.current) return;
    finishedRef.current = true;
    setIsFinished(true);
    setCountdown(null);

    // Tấm cuối cũng được giữ hình REVIEW_MS như các tấm trước rồi mới sang bước sau
    if (reviewSeqRef.current !== null) {
      const remaining = REVIEW_MS - (Date.now() - reviewShownAtRef.current);
      if (remaining > 0) await new Promise<void>((resolve) => setTimeout(resolve, remaining));
    }

    // Chờ các ảnh còn đang tải từ máy ảnh về (tấm nào quá hạn thì giữ ảnh tạm)
    if (pendingRef.current.size > 0) {
      setIsSaving(true);
      await Promise.race([
        new Promise<void>((resolve) => pendingWaitersRef.current.push(resolve)),
        new Promise<void>((resolve) => setTimeout(resolve, PENDING_SHOTS_TIMEOUT_MS)),
      ]);
    }
    const photos = [...photosRef.current];
    const paths = [...pathsRef.current];
    setTimeout(() => onAllShotsCaptured(photos, paths), 300);
  }, [onAllShotsCaptured]);

  /** Báo khách tấm vừa chụp không dùng được và sẽ chụp lại (gọi sau khi đã bỏ tấm lỗi khỏi danh sách). */
  const showCaptureError = (message: string) => {
    const reason = message.trim() || 'Máy ảnh gặp sự cố.';
    setCaptureError({ shot: photosRef.current.length + 1, reason });
    if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    errorTimerRef.current = setTimeout(() => setCaptureError(null), RETAKE_NOTICE_MS);
  };

  /**
   * Chụp một tấm, trả về toàn bộ danh sách ảnh sau khi thêm.
   * Máy ảnh báo lỗi thì không thêm ảnh (trừ khi mustProduce: khi đó dùng tạm khung live view).
   */
  const captureOne = useCallback((mustProduce = false): Promise<string[]> => {
    if (inFlightRef.current) return inFlightRef.current;

    const run = async (): Promise<string[]> => {
      const shotIndex = nextSeqRef.current++;
      // Tấm mới: ẩn ảnh xem lại của tấm trước để khách thấy live view
      hideReview();
      const fromDslr = sourceRef.current === 'dslr';
      setIsCapturing(true);
      setIsFlashing(true);
      setTimeout(() => setIsFlashing(false), 250);
      playShutterSound();

      let photoData = '';
      let filePath = '';
      const video = videoRef.current;
      if (!fromDslr && video && video.videoWidth > 0) {
        const canvas = document.createElement('canvas');
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        const ctx = canvas.getContext('2d');
        if (ctx) {
          // Không lật: ảnh và live view đều theo đúng chiều thật
          ctx.drawImage(video, 0, 0);
          photoData = canvas.toDataURL('image/jpeg', 0.95);
        }
      }

      // Hiện ngay khoảnh khắc vừa bấm trong REVIEW_MS (webcam: chính ảnh chụp; máy ảnh: khung live view đúng lúc bấm).
      // Trong lúc máy lấy nét / nhả màn trập / truyền ảnh, live view không có khung mới nên nếu không phủ lên thì màn
      // hình trông như bị đơ. Ảnh thật tải về sau chỉ được lưu lại, không hiện chen vào nữa.
      const snapshot = fromDslr ? liveFrameRef.current : photoData;
      if (snapshot && !finishedRef.current) showReview(snapshot, shotIndex);

      let pending = false;
      try {
        const result = await cameraApi.captureShot(shotIndex, photoData);
        if (result?.filePath && !result.filePath.startsWith('mock://')) filePath = result.filePath;
        // Máy ảnh: màn trập đã nhả, ảnh thật tới sau qua shot_ready; tạm dùng khung live view
        pending = !!result?.pending;
        if ((fromDslr || !photoData) && result?.base64) {
          photoData = result.base64.startsWith('data:') ? result.base64 : `data:image/jpeg;base64,${result.base64}`;
        }
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        console.warn('[FavoriLiveViewScreen] capture_shot failed:', message);
        if (fromDslr && !mustProduce) {
          hideReview();
          showCaptureError(message);
          setIsCapturing(false);
          return photosRef.current;
        }
      }

      if (!photoData) photoData = liveFrameRef.current || placeholderShot(shotIndex);

      if (pending) {
        pendingRef.current.add(shotIndex);
        // Khung live view nhận trước lúc này là khung cũ (trước khi máy truyền ảnh), không tính
        if (reviewSeqRef.current === shotIndex) freshFramesRef.current = 0;
      }
      photosRef.current = [...photosRef.current, photoData];
      pathsRef.current = [...pathsRef.current, filePath];
      seqsRef.current = [...seqsRef.current, shotIndex];
      setShotsTaken(photosRef.current.length);
      setIsCapturing(false);
      return photosRef.current;
    };

    const promise = run().finally(() => {
      inFlightRef.current = null;
    });
    inFlightRef.current = promise;
    return promise;
  }, []);

  // Chế độ tự động: đếm ngược rồi chụp, lặp tới khi đủ số tấm
  useEffect(() => {
    if (isRemote || countdown === null) return;
    if (countdown > 0) {
      // Giây cuối: máy ảnh lấy nét trước để hết giờ là chụp ngay
      if (countdown === 1 && sourceRef.current === 'dslr') cameraApi.prefocus();
      const timer = setTimeout(() => setCountdown((c) => (c !== null ? c - 1 : null)), 1000);
      return () => clearTimeout(timer);
    }
    captureOne().then((photos) => {
      if (photos.length >= totalShots) {
        finish();
        return;
      }
      if (finishedRef.current) return;
      // Chỉ đếm tấm sau khi ảnh vừa chụp hiện xong và khách đã thấy lại live view
      const waiting = reviewSeqRef.current !== null;
      let started = false;
      let safety: ReturnType<typeof setTimeout> | undefined;
      const start = () => {
        if (started || finishedRef.current) return;
        started = true;
        if (safety) clearTimeout(safety);
        if (afterReviewRef.current === start) afterReviewRef.current = null;
        setCountdown(countdownSec);
      };
      if (waiting) {
        afterReviewRef.current = start;
        safety = setTimeout(start, REVIEW_WAIT_MAX_MS);
      } else {
        start();
      }
    });
  }, [countdown, isRemote]);

  // Chế độ điều khiển từ xa: mỗi tín hiệu từ nút bấm (phím) chụp ngay một tấm
  const handleRemoteShot = async () => {
    if (inFlightRef.current || finishedRef.current) return;
    const photos = await captureOne();
    if (photos.length >= totalShots) finish();
  };
  const handleRemoteShotRef = useRef(handleRemoteShot);
  handleRemoteShotRef.current = handleRemoteShot;
  const finishRef = useRef(finish);
  finishRef.current = finish;

  // Nút bấm rời giả lập bàn phím (nút USB, bút trình chiếu, remote Bluetooth)
  useEffect(() => {
    if (!isRemote) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.repeat || !REMOTE_TRIGGER_KEYS.has(e.key)) return;
      e.preventDefault();
      handleRemoteShotRef.current();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isRemote]);

  // Điều khiển từ xa của máy ảnh (Canon BR-E1...) hoặc nút chụp trên thân máy: máy tự chụp, host tải ảnh về rồi báo sang
  useEffect(() => {
    if (!isRemote || source !== 'dslr') return;
    cameraApi.setRemoteShutter(true);
    const off = cameraApi.onRemoteShot((shot) => {
      if (finishedRef.current) return;
      setIsFlashing(true);
      setTimeout(() => setIsFlashing(false), 250);
      playShutterSound();
      const photo = shot.base64.startsWith('data:') ? shot.base64 : `data:image/jpeg;base64,${shot.base64}`;
      photosRef.current = [...photosRef.current, photo];
      pathsRef.current = [...pathsRef.current, shot.filePath || ''];
      seqsRef.current = [...seqsRef.current, shot.shotIndex];
      // Số tấm do host cấp: tấm chụp bằng phím sau đó không được trùng số
      nextSeqRef.current = Math.max(nextSeqRef.current, shot.shotIndex + 1);
      setShotsTaken(photosRef.current.length);
      showReview(photo, shot.shotIndex);
      if (photosRef.current.length >= totalShots) finishRef.current();
    });
    return () => {
      off();
      cameraApi.setRemoteShutter(false);
    };
  }, [isRemote, source, totalShots]);

  // Chế độ nút bấm: hết giờ thì đi tiếp với ảnh đã có (chưa có tấm nào thì chụp một tấm)
  const remoteTimeLeft = useCountdown(
    remoteTimeoutSec,
    async () => {
      if (inFlightRef.current) await inFlightRef.current;
      if (photosRef.current.length === 0) await captureOne(true);
      finish();
    },
    { paused: !isRemote || isFinished },
  );

  const viewfinderAspect = source === 'dslr' ? DSLR_ASPECT : WEBCAM_ASPECT;

  // Phần khung ngắm sẽ lọt vào ô của frame (ảnh đặt phủ kín ô, cắt giữa): tỉ lệ theo bề rộng / bề cao khung ngắm
  const guide =
    guideAspect && guideAspect > 0
      ? {
          sw: Math.min(1, guideAspect / viewfinderAspect),
          sh: Math.min(1, viewfinderAspect / guideAspect),
        }
      : null;

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#f5f0ea] flex flex-col p-6 select-none">
      <div
        className="absolute inset-0 pointer-events-none bg-cover bg-center z-0 opacity-90"
        style={{ backgroundImage: `url('/favori/Background.png')` }}
      />

      {isFlashing && (
        <div className="absolute inset-0 bg-white z-50 pointer-events-none animate-shutter-flash" />
      )}

      <header className="relative z-10 w-full grid grid-cols-[1fr_auto_1fr] items-start px-6 pt-2">
        <div className="flex flex-col items-center justify-self-start">
          <img src="/favori/logo.png" alt="Favori" className="h-16 object-contain" />
          <span className="text-[#7a0000] text-base font-medium tracking-wider uppercase mt-0.5">
            BƯỚC 7/11
          </span>
        </div>

        <h2 className="text-3xl md:text-4xl font-lobster text-[#1f1610] pt-2">
          Nhìn vào khung hình nhé
        </h2>

        {/* "Photobooth by Favori" đã có sẵn trong Background.png */}
        <div />
      </header>

      <main className="relative z-10 flex-1 min-h-0 flex items-stretch justify-center gap-6 w-full px-2">
        {/* Cột trống cùng bề rộng với cột phải để khung ngắm nằm đúng giữa màn hình */}
        <div className="w-40 flex-shrink-0" aria-hidden />

        {/* Vùng chứa khung ngắm: khung lớn tối đa trong vùng này, giữ đúng tỉ lệ cảm biến (như FavoriCurrent) */}
        <div className="flex-1 min-w-0 min-h-0 flex items-center justify-center" style={{ containerType: 'size' }}>
        <div
          className="relative bg-neutral-950 overflow-hidden flex items-center justify-center"
          style={{
            aspectRatio: String(viewfinderAspect),
            width: `min(100cqw, calc(100cqh * ${viewfinderAspect}))`,
          }}
        >
          <video
            ref={videoRef}
            autoPlay
            playsInline
            muted
            className={`w-full h-full object-cover ${source === 'dslr' ? 'hidden' : 'block'}`}
          />
          {source === 'dslr' && (
            <img ref={liveImgRef} alt="" className="w-full h-full object-cover" />
          )}

          {/* Đường kẻ căn khung: phần sáng trong đường viền trắng là phần ảnh sẽ nằm trong ô của frame */}
          {guide && (
            <div className="absolute inset-0 pointer-events-none">
              {guide.sw < 0.995 && (
                <>
                  <div className="absolute inset-y-0 left-0 bg-black/60" style={{ width: `${((1 - guide.sw) / 2) * 100}%` }} />
                  <div className="absolute inset-y-0 right-0 bg-black/60" style={{ width: `${((1 - guide.sw) / 2) * 100}%` }} />
                </>
              )}
              {guide.sh < 0.995 && (
                <>
                  <div className="absolute inset-x-0 top-0 bg-black/60" style={{ height: `${((1 - guide.sh) / 2) * 100}%` }} />
                  <div className="absolute inset-x-0 bottom-0 bg-black/60" style={{ height: `${((1 - guide.sh) / 2) * 100}%` }} />
                </>
              )}
              <div
                className="absolute border-2 border-white/90"
                style={{
                  left: `${((1 - guide.sw) / 2) * 100}%`,
                  top: `${((1 - guide.sh) / 2) * 100}%`,
                  width: `${guide.sw * 100}%`,
                  height: `${guide.sh * 100}%`,
                }}
              />
            </div>
          )}

          {/* Ảnh vừa chụp: phủ khung ngắm 2 giây rồi quay lại live view */}
          {reviewPhoto && (
            <img
              src={reviewPhoto}
              alt="Ảnh vừa chụp"
              className="absolute inset-0 w-full h-full object-cover z-20 pointer-events-none select-none"
            />
          )}

          {!cameraReady && (
            <div className="absolute inset-0 flex items-center justify-center bg-neutral-900 text-white font-lobster text-xl">
              Đang kết nối camera ...
            </div>
          )}

          {isSaving && (
            <div className="absolute inset-0 flex items-center justify-center bg-black/55 text-white font-lobster text-2xl z-20">
              Đang lưu ảnh ...
            </div>
          )}

          {/* Tấm vừa chụp bị bỏ: báo khách chụp lại (nằm trên ảnh xem lại và số đếm ngược) */}
          {captureError && (
            <div className="absolute top-6 inset-x-0 flex justify-center pointer-events-none z-40">
              <div
                role="alert"
                className="flex items-center gap-4 max-w-[85%] px-7 py-4 rounded-2xl bg-[#7a0000]/95 text-white shadow-2xl"
              >
                <RotateCcw className="w-10 h-10 flex-shrink-0" />
                <div className="flex flex-col text-left">
                  <span className="text-2xl font-bold leading-tight">
                    Chụp lại tấm {Math.min(captureError.shot, totalShots)}/{totalShots}
                  </span>
                  <span className="text-base opacity-90 leading-snug">{captureError.reason}</span>
                  <span className="text-base font-semibold leading-snug mt-1">
                    {isRemote ? 'Bấm nút chụp để chụp lại tấm này.' : 'Máy sẽ đếm ngược và chụp lại ngay.'}
                  </span>
                </div>
              </div>
            </div>
          )}

          {/* Số đếm ngược mờ giữa khung ngắm */}
          {countdown !== null && countdown > 0 && (
            <div className="absolute inset-0 z-30 flex items-center justify-center pointer-events-none">
              <span
                key={countdown}
                className="text-white/45 font-bold leading-none tabular-nums animate-countdown"
                style={{ fontSize: '24vh', fontFamily: 'system-ui, sans-serif' }}
              >
                {countdown}
              </span>
            </div>
          )}
        </div>
        </div>

        {/* Cột phải: đồng hồ + số tấm đã chụp */}
        <aside className="w-40 flex-shrink-0 h-full flex flex-col items-center">
          <div className="h-24 flex flex-col items-center justify-start text-center">
            {isRemote && (
              <>
                <span className="text-[#c1121f] text-xs font-semibold leading-tight">
                  Thời gian còn lại
                </span>
                <span className="text-[#c1121f] text-2xl font-bold tabular-nums mt-1">
                  {remoteTimeLeft}s
                </span>
              </>
            )}
          </div>

          <div className="flex-1 flex flex-col items-center justify-center gap-8">
            <span className="text-[#1f1610] text-4xl font-bold tabular-nums">
              {shotsTaken}/{totalShots}
            </span>

            {isRemote && (
              <div
                className={`flex flex-col items-center gap-2 text-center text-[#7a0000] transition-opacity ${
                  isCapturing || isFinished ? 'opacity-40' : ''
                }`}
              >
                <Camera className="w-9 h-9" />
                <span className="text-sm font-semibold leading-snug">Bấm nút trên điều khiển để chụp</span>
              </div>
            )}
          </div>

          <div className="h-24" />
        </aside>
      </main>

      <footer className="relative z-10 w-full flex items-center justify-center pt-3 pb-4 text-center">
        <p className="font-lobster text-2xl md:text-3xl text-[#1f1610]">
          Vui lòng đứng trong khung hình nhé
        </p>
      </footer>
    </div>
  );
};
