import { TestBed } from '@angular/core/testing';
import { ColorService } from './color.service';

describe('ColorService', () => {
  let service: ColorService;
  let mockWorker: Worker;

  beforeEach(() => {
    mockWorker = {
      postMessage: jasmine.createSpy('postMessage'),
      onmessage: null,
      terminate: jasmine.createSpy('terminate')
    } as any;

    spyOn(window, 'Worker').and.returnValue(mockWorker);

    TestBed.configureTestingModule({});
    service = TestBed.inject(ColorService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with a worker', () => {
    expect(window.Worker).toHaveBeenCalled();
  });

  it('should use cached color if available', () => {
    const testUrl = 'test-image.jpg';
    const testColor = '#123456';
    
    // First call to cache the color
    service['colorCache'].set(testUrl, testColor);
    
    // Mock document methods
    spyOn(document.documentElement.style, 'setProperty');
    
    service.extractColor(testUrl);
    
    expect(service.accentColor()).toBe(testColor);
    expect(document.documentElement.style.setProperty).toHaveBeenCalledWith('--accent', testColor);
    expect(mockWorker.postMessage).not.toHaveBeenCalled();
  });

  it('should post message to worker for uncached images', () => {
    const testUrl = 'test-image.jpg';
    service.extractColor(testUrl);
    expect(mockWorker.postMessage).toHaveBeenCalledWith(testUrl);
  });

  it('should handle worker success response', () => {
    const testColor = '#123456';
    const testUrl = 'test-image.jpg';
    
    spyOn(document.documentElement.style, 'setProperty');
    
    // Simulate worker response
    mockWorker.onmessage!({ data: { color: testColor, imageUrl: testUrl } } as MessageEvent);
    
    expect(service.accentColor()).toBe(testColor);
    expect(service['colorCache'].get(testUrl)).toBe(testColor);
    expect(document.documentElement.style.setProperty).toHaveBeenCalledWith('--accent', testColor);
  });

  it('should handle worker error response', () => {
    spyOn(console, 'error');
    spyOn(document.documentElement.style, 'setProperty');
    
    // Simulate worker error
    mockWorker.onmessage!({ data: { error: 'Test error' } } as MessageEvent);
    
    expect(console.error).toHaveBeenCalled();
    expect(service.accentColor()).toBe('#000000');
    expect(document.documentElement.style.setProperty).toHaveBeenCalledWith('--accent', '#000000');
  });

  it('should adjust color brightness correctly', () => {
    const color = '#FF8800';
    const darkerColor = service['adjustColorBrightness'](color, -0.2);
    const brighterColor = service['adjustColorBrightness'](color, 0.2);
    
    expect(darkerColor.toLowerCase()).toBe('#cc6c00');
    expect(brighterColor.toLowerCase()).toBe('#ffa300');
  });

  it('should terminate worker on destroy', () => {
    service.ngOnDestroy();
    expect(mockWorker.terminate).toHaveBeenCalled();
  });
}); 