import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import {
  ActivatedRoute,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
import { of } from 'rxjs';
import { ArtistDetailPageComponent } from './artist-detail-page.component';
import { BrowseService } from '../../browse.service';
import type { ArtistDetail } from '../../../../core/api/api.service';

const detail: ArtistDetail = {
  id: 'artist-1',
  name: 'Pink Floyd',
  albums: [
    {
      id: 'album-1',
      title: 'The Wall',
      artist: 'Pink Floyd',
      artistId: 'artist-1',
      year: 1979,
      imageRef: '/api/artwork/album-1?size=256',
      trackCount: 2,
    },
  ],
};

describe('ArtistDetailPageComponent', () => {
  let fixture: ComponentFixture<ArtistDetailPageComponent>;
  let browse: jasmine.SpyObj<BrowseService>;
  let artist: ReturnType<typeof signal<ArtistDetail | null>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let error: ReturnType<typeof signal<string | null>>;

  function setUp(paramId = 'artist-1') {
    artist = signal<ArtistDetail | null>(null);
    loading = signal(false);
    error = signal<string | null>(null);
    browse = jasmine.createSpyObj('BrowseService', ['loadArtist'], {
      artist,
      artistLoading: loading,
      artistError: error,
    });
    TestBed.configureTestingModule({
      imports: [ArtistDetailPageComponent],
      providers: [
        provideRouter([]),
        { provide: BrowseService, useValue: browse },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: paramId })) },
        },
      ],
    });
    fixture = TestBed.createComponent(ArtistDetailPageComponent);
    fixture.detectChanges();
  }

  it('loads the artist from the route param on init', () => {
    setUp('artist-7');
    expect(browse.loadArtist).toHaveBeenCalledWith('artist-7');
  });

  it('renders the artist name and an album card per album', () => {
    setUp();
    artist.set(detail);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Pink Floyd',
    );
    const cards = fixture.debugElement.queryAll(By.css('app-album-card'));
    expect(cards.length).toBe(1);
  });

  it('shows the error state when the artist is missing', () => {
    setUp();
    error.set('Artist not found.');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__error'))).toBeTruthy();
  });
});
