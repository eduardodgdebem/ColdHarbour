self.onmessage = async (e: MessageEvent) => {
  const imageUrl = e.data;
  
  try {
    const canvas = new OffscreenCanvas(50, 50);
    const ctx = canvas.getContext('2d');
    
    if (!ctx) {
      self.postMessage({ error: 'Could not get canvas context', imageUrl });
      return;
    }

    const response = await fetch(imageUrl);
    const blob = await response.blob();
    const imageBitmap = await createImageBitmap(blob);
    
    ctx.drawImage(imageBitmap, 0, 0, 50, 50);
    const imageData = ctx.getImageData(0, 0, 50, 50).data;
    
    let r = 0, g = 0, b = 0, count = 0;
    
    for (let i = 0; i < imageData.length; i += 4) {
      if (imageData[i + 3] < 128) continue;
      
      r += imageData[i];
      g += imageData[i + 1];
      b += imageData[i + 2];
      count++;
    }
    
    if (count === 0) {
      self.postMessage({ error: 'No visible pixels found in image', imageUrl });
      return;
    }
    
    r = Math.round(r / count);
    g = Math.round(g / count);
    b = Math.round(b / count);
    
    const brightness = (r * 299 + g * 587 + b * 114) / 1000;
    if (brightness < 128) {
      const factor = 5 - ((brightness / 128) * 3.8);
      r = Math.min(255, Math.round(r * factor));
      g = Math.min(255, Math.round(g * factor));
      b = Math.min(255, Math.round(b * factor));
    }
    
    const color = `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`;
    
    self.postMessage({ color, imageUrl });
  } catch (error: unknown) {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error occurred';
    self.postMessage({ error: errorMessage, imageUrl });
  }
}; 