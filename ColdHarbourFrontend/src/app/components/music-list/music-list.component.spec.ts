import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MusicListComponent } from './music-list.component';
import { MusicService } from '../../services/music.service';
import type { Music } from '../../services/music.service';

describe('MusicListComponent', () => {
  let component: MusicListComponent;
  let fixture: ComponentFixture<MusicListComponent>;
  let musicService: jasmine.SpyObj<MusicService>;

  const mockMusic: Music = {
    id: 1,
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/test.mp3',
    imageRef: '/test.jpg'
  };

  beforeEach(async () => {
    const spy = jasmine.createSpyObj('MusicService', ['selectMusic', 'isCurrentMusic']);
    
    await TestBed.configureTestingModule({
      imports: [MusicListComponent],
      providers: [
        { provide: MusicService, useValue: spy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MusicListComponent);
    component = fixture.componentInstance;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call selectMusic when selecting a music item', () => {
    musicService.selectMusic.and.returnValue(undefined);
    
    component.selectMusic(mockMusic);
    
    expect(musicService.selectMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should check if music is current music', () => {
    musicService.isCurrentMusic.and.returnValue(true);
    
    const result = component.isCurrentMusic(mockMusic);
    
    expect(result).toBe(true);
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should return false when music is not current', () => {
    musicService.isCurrentMusic.and.returnValue(false);
    
    const result = component.isCurrentMusic(mockMusic);
    
    expect(result).toBe(false);
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });
});
