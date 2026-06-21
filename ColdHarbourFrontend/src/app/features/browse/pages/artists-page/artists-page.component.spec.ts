import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { ArtistsPageComponent } from './artists-page.component';
import { BrowseService } from '../../browse.service';
import type { ArtistSummary } from '../../../../core/api/api.service';

function artist(
  id: string,
  overrides: Partial<ArtistSummary> = {},
): ArtistSummary {
  return { id, name: `Artist ${id}`, albumCount: 2, ...overrides };
}

describe('ArtistsPageComponent', () => {
  let fixture: ComponentFixture<ArtistsPageComponent>;
  let browse: jasmine.SpyObj<BrowseService>;
  let artists: ReturnType<typeof signal<ArtistSummary[]>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let error: ReturnType<typeof signal<string | null>>;

  function setUp() {
    artists = signal<ArtistSummary[]>([]);
    loading = signal(false);
    error = signal<string | null>(null);
    browse = jasmine.createSpyObj('BrowseService', ['loadArtists'], {
      artists,
      artistsLoading: loading,
      artistsError: error,
    });
    TestBed.configureTestingModule({
      imports: [ArtistsPageComponent],
      providers: [
        provideRouter([]),
        { provide: BrowseService, useValue: browse },
      ],
    });
    fixture = TestBed.createComponent(ArtistsPageComponent);
    fixture.detectChanges();
  }

  it('loads artists on init', () => {
    setUp();
    expect(browse.loadArtists).toHaveBeenCalled();
  });

  it('renders an artist card per artist linking to detail', () => {
    setUp();
    artists.set([artist('1'), artist('2')]);
    fixture.detectChanges();
    const cards = fixture.debugElement.queryAll(By.css('a.artist-card'));
    expect(cards.length).toBe(2);
    expect(cards[0].nativeElement.getAttribute('href')).toBe('/artists/1');
  });

  it('shows the empty state when there are no artists', () => {
    setUp();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__empty'))).toBeTruthy();
  });

  it('shows the error state', () => {
    setUp();
    error.set('Failed to load artists.');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__error'))).toBeTruthy();
  });
});
