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
      'updateAlbum',
      'updateTrack',
      'renameArtist',
      'uploadAlbumCover',
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

  describe('saveAlbum', () => {
    it('updates metadata, reloads the album, and signals success', () => {
      api.updateAlbum.and.returnValue(of(void 0));
      api.getAlbum.and.returnValue(of(albumDetail));
      const onSuccess = jasmine.createSpy('onSuccess');

      service.saveAlbum(
        'album-1',
        { title: 'The Wall', year: 1979 },
        null,
        onSuccess,
      );

      expect(api.updateAlbum).toHaveBeenCalledWith('album-1', {
        title: 'The Wall',
        year: 1979,
      });
      expect(api.getAlbum).toHaveBeenCalledWith('album-1');
      expect(onSuccess).toHaveBeenCalled();
      expect(service.saving()).toBeFalse();
    });

    it('uploads the cover when a file is provided', () => {
      api.updateAlbum.and.returnValue(of(void 0));
      api.uploadAlbumCover.and.returnValue(of(void 0));
      api.getAlbum.and.returnValue(of(albumDetail));
      const file = new File(['x'], 'cover.jpg', { type: 'image/jpeg' });

      service.saveAlbum('album-1', { title: 'The Wall', year: 1979 }, file);

      expect(api.uploadAlbumCover).toHaveBeenCalledWith('album-1', file);
    });

    it('sets saveError and does not signal success on failure', () => {
      api.updateAlbum.and.returnValue(throwError(() => new Error('400')));
      const onSuccess = jasmine.createSpy('onSuccess');

      service.saveAlbum('album-1', { title: '', year: null }, null, onSuccess);

      expect(service.saveError()).toBeTruthy();
      expect(service.saving()).toBeFalse();
      expect(onSuccess).not.toHaveBeenCalled();
    });
  });

  describe('saveTrack', () => {
    it('updates the track, reloads the parent album, and signals success', () => {
      api.updateTrack.and.returnValue(of(void 0));
      api.getAlbum.and.returnValue(of(albumDetail));
      const onSuccess = jasmine.createSpy('onSuccess');

      service.saveTrack(
        'album-1',
        'track-1',
        { title: 'Hey', trackNumber: 1 },
        onSuccess,
      );

      expect(api.updateTrack).toHaveBeenCalledWith('track-1', {
        title: 'Hey',
        trackNumber: 1,
      });
      expect(api.getAlbum).toHaveBeenCalledWith('album-1');
      expect(onSuccess).toHaveBeenCalled();
    });
  });

  describe('saveArtist', () => {
    it('renames the artist, reloads it, and signals success', () => {
      api.renameArtist.and.returnValue(of(void 0));
      api.getArtist.and.returnValue(of(artistDetail));
      const onSuccess = jasmine.createSpy('onSuccess');

      service.saveArtist('artist-1', { name: 'Pink Floyd' }, onSuccess);

      expect(api.renameArtist).toHaveBeenCalledWith('artist-1', {
        name: 'Pink Floyd',
      });
      expect(api.getArtist).toHaveBeenCalledWith('artist-1');
      expect(onSuccess).toHaveBeenCalled();
    });
  });
});
