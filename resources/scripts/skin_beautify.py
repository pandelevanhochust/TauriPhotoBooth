import sys
import os
import argparse

def beautify_image(input_path: str, output_path: str, strength: float = 0.6):
    print(f"[AI Beautify] Processing: {input_path} -> {output_path} (strength={strength})")
    
    if not os.path.exists(input_path):
        print(f"Error: Input file does not exist: {input_path}", file=sys.stderr)
        sys.exit(1)

    output_dir = os.path.dirname(output_path)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir, exist_ok=True)

    # Attempt OpenCV + MediaPipe
    try:
        import cv2
        import numpy as np

        img = cv2.imread(input_path)
        if img is None:
            raise ValueError("Failed to decode image with cv2")

        # Bilateral filter preserves edges while smoothing skin surfaces
        diameter = int(9 * strength) + 3
        sigma_color = int(75 * strength) + 20
        sigma_space = int(75 * strength) + 20
        
        smoothed = cv2.bilateralFilter(img, diameter, sigma_color, sigma_space)
        
        # Soft blend with original to keep natural skin texture
        result = cv2.addWeighted(img, 1.0 - strength * 0.7, smoothed, strength * 0.7, 0)
        
        cv2.imwrite(output_path, result, [int(cv2.IMWRITE_JPEG_QUALITY), 95])
        print("[AI Beautify] Completed with OpenCV Bilateral filter.")
        return
    except ImportError:
        pass

    # Fallback to PIL (Pillow)
    try:
        from PIL import Image, ImageFilter, ImageEnhance

        img = Image.open(input_path)
        # Apply smooth filter
        blurred = img.filter(ImageFilter.SMOOTH_MORE)
        # Blend
        blended = Image.blend(img, blurred, alpha=min(0.5, strength * 0.6))
        # Slight brightness / contrast boost for radiant portrait look
        enhancer = ImageEnhance.Color(blended)
        enhanced = enhancer.enhance(1.05)
        
        enhanced.save(output_path, quality=95)
        print("[AI Beautify] Completed with PIL enhancement.")
        return
    except Exception as ex:
        print(f"[AI Beautify] Warning: Python imaging libraries not available: {ex}. Copying original.", file=sys.stderr)
        import shutil
        shutil.copyfile(input_path, output_path)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="HihiBooth AI Skin Beautify")
    parser.add_argument("--input", required=True, help="Input raw image path")
    parser.add_argument("--output", required=True, help="Output beautified image path")
    parser.add_argument("--strength", type=float, default=0.6, help="Smoothing strength 0.0 to 1.0")
    
    args = parser.parse_args()
    beautify_image(args.input, args.output, args.strength)
