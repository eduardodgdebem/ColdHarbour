import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AlbumsPageComponent } from './albums-page.component';
import { BrowseService } from '../../browse.service';
import type { AlbumSummary } from '../../../../core/api/api.service';

function album(
  id: string,
  overrides: Partial<AlbumSummary> = {},
): AlbumSummary {
  return {
    id,
    title: `Album ${id}`,
    artist: `Artist ${id}`,
    artistId: `artist-${id}`,
    year: 2000,
    imageRef: `/api/artwork/${id}?size=256`,
    trackCount: 3,
    ...overrides,
  };
}

describe('AlbumsPageComponent', () => {
  let fixture: ComponentFixture<AlbumsPageComponent>;
  let browse: jasmine.SpyObj<BrowseService>;
  let albums: ReturnType<typeof signal<AlbumSummary[]>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let error: ReturnType<typeof signal<string | null>>;

  function setUp() {
    albums = signal<AlbumSummary[]>([]);
    loading = signal(false);
    error = signal<string | null>(null);
    browse = jasmine.createSpyObj('BrowseService', ['loadAlbums'], {
      albums,
      albumsLoading: loading,
      albumsError: error,
    });
    TestBed.configureTestingModule({
      imports: [AlbumsPageComponent],
      providers: [
        provideRouter([]),
        { provide: BrowseService, useValue: browse },
      ],
    });
    fixture = TestBed.createComponent(AlbumsPageComponent);
    fixture.detectChanges();
  }

  it('loads albums on init', () => {
    setUp();
    expect(browse.loadAlbums).toHaveBeenCalled();
  });

  it('renders an album card per album', () => {
    setUp();
    albums.set([album('1'), album('2')]);
    fixture.detectChanges();
    expect(fixture.debugElement.queryAll(By.css('app-album-card')).length).toBe(
      2,
    );
  });

  it('shows the loading state', () => {
    setUp();
    loading.set(true);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__loading'))).toBeTruthy();
  });

  it('shows the empty state when there are no albums', () => {
    setUp();
    albums.set([]);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__empty'))).toBeTruthy();
  });

  it('shows the error state', () => {
    setUp();
    error.set('Failed to load albums.');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__error'))).toBeTruthy();
  });
});
