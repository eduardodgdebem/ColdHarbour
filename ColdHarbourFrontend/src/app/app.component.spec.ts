import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';
import { PlayerComponent } from './components/player/player.component';
import { MusicListComponent } from './components/music-list/music-list.component';
import { CommonModule } from '@angular/common';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        AppComponent,
        PlayerComponent,
        MusicListComponent,
        CommonModule
      ]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should initialize with undefined background image', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.backgroundImg).toBeUndefined();
  });

  it('should render player and music list components', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    
    expect(compiled.querySelector('app-player')).toBeTruthy();
    expect(compiled.querySelector('app-music-list')).toBeTruthy();
  });
});
