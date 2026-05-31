const CANVAS_SIZE = 50;
const ALPHA_THRESHOLD = 128;
const DARK_LUMA_THRESHOLD = 128;
const BRIGHT_LUMA_THRESHOLD = 200;
const BRIGHTNESS_MAX_BOOST = 4;
const BRIGHTNESS_MIN_BOOST = 1.2;
const BRIGHTNESS_MIN_FACTOR = 0.4;
const LOW_SATURATION_THRESHOLD = 0.3;
const HIGH_SATURATION_THRESHOLD = 0.8;
const SATURATION_BOOST_FACTOR = 3;
const SATURATION_MIN_FACTOR = 0.6;

function linearDampen(value: number, threshold: number, ceiling: number, minFactor: number): number {
  return 1 - ((value - threshold) / (ceiling - threshold)) * (1 - minFactor);
}

async function fetchImageBitmap(url: string): Promise<ImageBitmap> {
  const response = await fetch(url);
  const blob = await response.blob();
  return createImageBitmap(blob);
}

function averageVisibleColor(
  data: Uint8ClampedArray,
): { r: number; g: number; b: number } | null {
  let r = 0, g = 0, b = 0, count = 0;
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < ALPHA_THRESHOLD) continue;
    r += data[i];
    g += data[i + 1];
    b += data[i + 2];
    count++;
  }
  if (count === 0) return null;
  return { r: Math.round(r / count), g: Math.round(g / count), b: Math.round(b / count) };
}

function normalizeBrightness(
  r: number,
  g: number,
  b: number,
  maxBoost = BRIGHTNESS_MAX_BOOST,
  minFactor = BRIGHTNESS_MIN_FACTOR,
): { r: number; g: number; b: number } {
  const luma = (r * 299 + g * 587 + b * 114) / 1000;

  if (luma < DARK_LUMA_THRESHOLD) {
    const factor = maxBoost - (luma / DARK_LUMA_THRESHOLD) * (maxBoost - BRIGHTNESS_MIN_BOOST);
    return {
      r: Math.min(255, Math.round(r * factor)),
      g: Math.min(255, Math.round(g * factor)),
      b: Math.min(255, Math.round(b * factor)),
    };
  }

  if (luma > BRIGHT_LUMA_THRESHOLD) {
    const factor = linearDampen(luma, BRIGHT_LUMA_THRESHOLD, 255, minFactor);
    return {
      r: Math.round(r * factor),
      g: Math.round(g * factor),
      b: Math.round(b * factor),
    };
  }

  return { r, g, b };
}

function hue2rgb(p: number, q: number, t: number): number {
  if (t < 0) t += 1;
  if (t > 1) t -= 1;
  if (t < 1 / 6) return p + (q - p) * 6 * t;
  if (t < 1 / 2) return q;
  if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
  return p;
}

function normalizeSaturation(
  r: number,
  g: number,
  b: number,
  boostFactor = SATURATION_BOOST_FACTOR,
  minFactor = SATURATION_MIN_FACTOR,
): { r: number; g: number; b: number } {
  const rn = r / 255, gn = g / 255, bn = b / 255;
  const max = Math.max(rn, gn, bn), min = Math.min(rn, gn, bn);
  const l = (max + min) / 2;

  if (max === min) return { r, g, b }; // achromatic — nothing to normalize

  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h = 0;
  if (max === rn) h = ((gn - bn) / d + (gn < bn ? 6 : 0)) / 6;
  else if (max === gn) h = ((bn - rn) / d + 2) / 6;
  else h = ((rn - gn) / d + 4) / 6;

  let sNorm: number;
  if (s < LOW_SATURATION_THRESHOLD) {
    sNorm = Math.min(1, s * boostFactor);
  } else if (s > HIGH_SATURATION_THRESHOLD) {
    sNorm = s * linearDampen(s, HIGH_SATURATION_THRESHOLD, 1, minFactor);
  } else {
    sNorm = s;
  }

  const q = l < 0.5 ? l * (1 + sNorm) : l + sNorm - l * sNorm;
  const p = 2 * l - q;
  return {
    r: Math.round(hue2rgb(p, q, h + 1 / 3) * 255),
    g: Math.round(hue2rgb(p, q, h) * 255),
    b: Math.round(hue2rgb(p, q, h - 1 / 3) * 255),
  };
}

function toHex(r: number, g: number, b: number): string {
  return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`;
}

self.onmessage = async (e: MessageEvent) => {
  const imageUrl: string = e.data;
  try {
    const canvas = new OffscreenCanvas(CANVAS_SIZE, CANVAS_SIZE);
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      self.postMessage({ error: 'Could not get canvas context', imageUrl });
      return;
    }

    const bitmap = await fetchImageBitmap(imageUrl);
    ctx.drawImage(bitmap, 0, 0, CANVAS_SIZE, CANVAS_SIZE);
    const imageData = ctx.getImageData(0, 0, CANVAS_SIZE, CANVAS_SIZE).data;

    const avg = averageVisibleColor(imageData);
    if (!avg) {
      self.postMessage({ error: 'No visible pixels found in image', imageUrl });
      return;
    }

    const brightened = normalizeBrightness(avg.r, avg.g, avg.b, BRIGHTNESS_MAX_BOOST, BRIGHTNESS_MIN_FACTOR);
    const { r, g, b } = normalizeSaturation(brightened.r, brightened.g, brightened.b, SATURATION_BOOST_FACTOR, SATURATION_MIN_FACTOR);
    self.postMessage({ color: toHex(r, g, b), imageUrl });
  } catch (error: unknown) {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error occurred';
    self.postMessage({ error: errorMessage, imageUrl });
  }
};
