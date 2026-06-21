import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BrowseService } from './browse.service';
import {
  ApiService,
  type AlbumDetail,
  type AlbumSummary,
  type ArtistDetail,
  type ArtistSummary,
} from '../../core/api/api.service';

const album: AlbumSummary = {
  id: 'album-1',
  title: 'The Wall',
  artist: 'Pink Floyd',
  artistId: 'artist-1',
  year: 1979,
  imageRef: '/api/artwork/album-1?size=256&v=abc',
  trackCount: 2,
};

const albumDetail: AlbumDetail = { ...album, tracks: [] };
const artist: ArtistSummary = {
  id: 'artist-1',
  name: 'Pink Floyd',
  albumCount: 1,
};
const artistDetail: ArtistDetail = {
  id: 'artist-1',
  name: 'Pink Floyd',
  albums: [album],
};

describe('BrowseService', () => {
  let service: BrowseService;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj('ApiService', [
      'getAlbums',
      'getAlbum',
      'getArtists',
      'getArtist',
    ]);
    TestBed.configureTestingModule({
      providers: [BrowseService, { provide: ApiService, useValue: api }],
    });
    service = TestBed.inject(BrowseService);
  });

  it('loadAlbums populates albums and clears loading', () => {
    api.getAlbums.and.returnValue(of([album]));
    service.loadAlbums();
    expect(service.albums()).toEqual([album]);
    expect(service.albumsLoading()).toBeFalse();
    expect(service.albumsError()).toBeNull();
  });

  it('loadAlbums sets an error message on failure', () => {
    api.getAlbums.and.returnValue(throwError(() => new Error('boom')));
    service.loadAlbums();
    expect(service.albumsError()).toBeTruthy();
    expect(service.albumsLoading()).toBeFalse();
  });

  it('loadAlbum populates the album detail', () => {
    api.getAlbum.and.returnValue(of(albumDetail));
    service.loadAlbum('album-1');
    expect(api.getAlbum).toHaveBeenCalledWith('album-1');
    expect(service.album()).toEqual(albumDetail);
    expect(service.albumLoading()).toBeFalse();
  });

  it('loadAlbum sets an error on failure', () => {
    api.getAlbum.and.returnValue(throwError(() => new Error('404')));
    service.loadAlbum('missing');
    expect(service.albumError()).toBeTruthy();
    expect(service.album()).toBeNull();
  });

  it('loadArtists populates artists', () => {
    api.getArtists.and.returnValue(of([artist]));
    service.loadArtists();
    expect(service.artists()).toEqual([artist]);
    expect(service.artistsLoading()).toBeFalse();
  });

  it('loadArtist populates the artist detail', () => {
    api.getArtist.and.returnValue(of(artistDetail));
    service.loadArtist('artist-1');
    expect(api.getArtist).toHaveBeenCalledWith('artist-1');
    expect(service.artist()).toEqual(artistDetail);
  });

  it('loadArtist sets an error on failure', () => {
    api.getArtist.and.returnValue(throwError(() => new Error('404')));
    service.loadArtist('missing');
    expect(service.artistError()).toBeTruthy();
    expect(service.artist()).toBeNull();
  });
});
