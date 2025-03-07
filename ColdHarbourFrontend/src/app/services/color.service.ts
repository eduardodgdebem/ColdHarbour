import { ChangeDetectorRef, Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class ColorService {
  public accentColor = signal('');
  private worker: Worker;
  private colorCache: Map<string, string> = new Map();
  private defaultColor = '#000000';

  constructor() {
    this.worker = new Worker(new URL('../../assets/color-worker.ts', import.meta.url), { type: 'module' });
    
    this.worker.onmessage = (e: MessageEvent) => {
      if (e.data.error) {
        console.error('Worker error:', e.data.error);
        this.updateColor(this.defaultColor);
        return;
      }
      
      if (e.data.color && e.data.imageUrl) {
        this.colorCache.set(e.data.imageUrl, e.data.color);
        this.updateColor(e.data.color);
      }
    };
  }

  public extractColor(imageUrl: string): void {
    const cachedColor = this.colorCache.get(imageUrl);
    if (cachedColor) {
      this.updateColor(cachedColor);
      return;
    }

    this.worker.postMessage(imageUrl);
  }

  private updateColor(color: string): void {
    this.accentColor.set(color);
    document.documentElement.style.setProperty('--accent', color);
    // Add a darker variant for text contrast
    const darkerColor = this.adjustColorBrightness(color, -0.2);
    document.documentElement.style.setProperty('--accent-dark', darkerColor);
  }

  private adjustColorBrightness(color: string, factor: number): string {
    // Remove the # if present
    const hex = color.replace('#', '');
    
    // Convert hex to RGB
    const r = parseInt(hex.substring(0, 2), 16);
    const g = parseInt(hex.substring(2, 4), 16);
    const b = parseInt(hex.substring(4, 6), 16);
    
    // Adjust brightness
    const adjustBrightness = (value: number) => {
      return Math.min(255, Math.max(0, Math.round(value * (1 + factor))));
    };
    
    // Convert back to hex
    const newR = adjustBrightness(r).toString(16).padStart(2, '0');
    const newG = adjustBrightness(g).toString(16).padStart(2, '0');
    const newB = adjustBrightness(b).toString(16).padStart(2, '0');
    
    return `#${newR}${newG}${newB}`;
  }

  ngOnDestroy() {
    if (this.worker) {
      this.worker.terminate();
    }
  }
} 