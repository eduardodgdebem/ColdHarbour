import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MusicListComponent } from './music-list.component';
import { MusicService } from '../../../player/services/music.service';
import { PlaybackSessionService } from '../../../player/services/playback-session.service';
import { LibraryService } from '../../library.service';
import type { Music, Playlist } from '../../../../core/api/api.service';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';

describe('MusicListComponent', () => {
  let component: MusicListComponent;
  let fixture: ComponentFixture<MusicListComponent>;
  let musicService: jasmine.SpyObj<MusicService>;
  let libraryService: jasmine.SpyObj<LibraryService>;
  let playbackSpy: jasmine.SpyObj<PlaybackSessionService>;

  const mockMusic: Music = {
    id: 1,
    trackId: '33333333-0000-0000-0000-000000000001',
    albumId: '22222222-0000-0000-0000-000000000001',
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/api/stream/33333333-0000-0000-0000-000000000001',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    durationSeconds: 180,
  };

  const mockPlaylist: Playlist = {
    id: 1,
    name: 'Test Playlist',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    musics: [mockMusic],
  };

  beforeEach(async () => {
    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'isCurrentMusic'],
      {
        currentMusic: signal<Music | null>(null),
        currentPlayList: signal<Playlist | null>(mockPlaylist),
        isLoading: signal(false),
        error: signal<string | null>(null),
      },
    );

    const librarySpy = jasmine.createSpyObj(
      'LibraryService',
      ['uploadFile', 'deleteTrack', 'previewSync', 'applySync'],
      {
        isUploading: signal(false),
        uploadProgress: signal(0),
        syncDiff: signal(null),
      },
    );

    playbackSpy = jasmine.createSpyObj('PlaybackSessionService', [
      'setQueue',
      'addToQueue',
      'removeFromQueue',
      'reorderQueue',
      'clearQueue',
    ]);

    await TestBed.configureTestingModule({
      imports: [MusicListComponent],
      providers: [
        { provide: MusicService, useValue: musicSpy },
        { provide: LibraryService, useValue: librarySpy },
        { provide: PlaybackSessionService, useValue: playbackSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MusicListComponent);
    component = fixture.componentInstance;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    libraryService = TestBed.inject(
      LibraryService,
    ) as jasmine.SpyObj<LibraryService>;
    fixture.detectChanges();
  });

  it('sends addToQueue via the hub when the queue button is clicked', () => {
    const queueBtn = fixture.debugElement
      .query(By.css('.track .queue-btn'))
      .nativeElement as HTMLButtonElement;
    queueBtn.click();
    expect(playbackSpy.addToQueue).toHaveBeenCalledWith(mockMusic.trackId);
    // Click should not also start the track (stopPropagation).
    expect(playbackSpy.setQueue).not.toHaveBeenCalled();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('declares the queue via the hub when selecting a music item', () => {
    component.selectMusic(mockMusic);
    // Server-authoritative: the click pushes setQueue (the displayed list +
    // picked index). currentMusic is written later by the server-state effect.
    expect(playbackSpy.setQueue).toHaveBeenCalledWith([mockMusic.trackId], 0);
    expect(musicService.selectMusic).not.toHaveBeenCalled();
  });

  it('should check if music is current music', () => {
    musicService.isCurrentMusic.and.returnValue(true);
    expect(component.isCurrentMusic(mockMusic)).toBeTrue();
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should return false when music is not current', () => {
    musicService.isCurrentMusic.and.returnValue(false);
    expect(component.isCurrentMusic(mockMusic)).toBeFalse();
  });

  it('should display music list from service', () => {
    const musicList = [
      { ...mockMusic, id: 1, name: 'Song 1' },
      { ...mockMusic, id: 2, name: 'Song 2' },
    ];
    musicService.currentPlayList.set({ ...mockPlaylist, musics: musicList });
    fixture.detectChanges();

    const musicElements = fixture.debugElement.queryAll(By.css('.track'));
    expect(musicElements.length).toBe(2);
  });

  it('uses the musics input override when provided, ignoring the service playlist', () => {
    const override = [
      { ...mockMusic, id: 10, name: 'Override A' },
      { ...mockMusic, id: 11, name: 'Override B' },
      { ...mockMusic, id: 12, name: 'Override C' },
    ];
    fixture.componentRef.setInput('musics', override);
    fixture.detectChanges();

    const rows = fixture.debugElement.queryAll(By.css('.track'));
    expect(rows.length).toBe(3);
    const names = rows.map((r) =>
      r.query(By.css('.track-name')).nativeElement.textContent.trim(),
    );
    expect(names).toEqual(['Override A', 'Override B', 'Override C']);
  });

  it('renders an empty-state message when the override is an empty array', () => {
    fixture.componentRef.setInput('musics', []);
    fixture.componentRef.setInput('emptyMessage', 'NOTHING MATCHED');
    fixture.detectChanges();

    const stateMsg = fixture.debugElement.query(By.css('.state-msg'));
    expect(stateMsg.nativeElement.textContent.trim()).toBe('NOTHING MATCHED');
  });

  describe('delete flow', () => {
    it('does not show the delete-confirm modal by default', () => {
      expect(component.deleteCandidate()).toBeNull();
      expect(fixture.debugElement.query(By.css('.modal'))).toBeNull();
    });

    it('opens the delete-confirm modal with the track when the delete button is clicked', () => {
      const deleteBtn = fixture.debugElement
        .query(By.css('.track .delete-btn'))
        .nativeElement as HTMLButtonElement;
      deleteBtn.click();
      fixture.detectChanges();

      expect(component.deleteCandidate()).toEqual(mockMusic);
      const modal = fixture.debugElement.query(By.css('.modal'));
      expect(modal).toBeTruthy();
      expect(modal.nativeElement.textContent).toContain(mockMusic.name);
    });

    it('does not trigger row selection when the delete button is clicked', () => {
      const deleteBtn = fixture.debugElement
        .query(By.css('.track .delete-btn'))
        .nativeElement as HTMLButtonElement;
      deleteBtn.click();
      expect(playbackSpy.setQueue).not.toHaveBeenCalled();
    });

    it('calls libraryService.deleteTrack and clears the candidate when confirmed', () => {
      component.deleteCandidate.set(mockMusic);
      fixture.detectChanges();

      component.confirmDelete();
      expect(libraryService.deleteTrack).toHaveBeenCalledWith(
        mockMusic.trackId,
      );
      expect(component.deleteCandidate()).toBeNull();
    });

    it('does not call deleteTrack and clears the candidate when canceled', () => {
      component.deleteCandidate.set(mockMusic);
      fixture.detectChanges();

      component.cancelDelete();
      expect(libraryService.deleteTrack).not.toHaveBeenCalled();
      expect(component.deleteCandidate()).toBeNull();
    });
  });
});
