import { DestroyRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { DeviceService } from '../../devices/device.service';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';
import { PlaybackSessionService } from './playback-session.service';
import type { Music, Playlist } from '../../../core/api/api.service';

class MockWebSocket {
  static instances: MockWebSocket[] = [];
  static OPEN = 1;
  static CLOSED = 3;

  public readyState = MockWebSocket.OPEN;
  public onopen: (() => void) | null = null;
  public onmessage: ((e: { data: string }) => void) | null = null;
  public onclose: ((e: { code: number }) => void) | null = null;
  public sentMessages: unknown[] = [];

  constructor(public url: string) {
    MockWebSocket.instances.push(this);
    queueMicrotask(() => this.onopen?.());
  }

  send(payload: string): void {
    this.sentMessages.push(JSON.parse(payload));
  }

  close(): void {
    this.readyState = MockWebSocket.CLOSED;
  }
}

const track = (id: string, name = 'Track'): Music => ({
  id: Number.parseInt(id.slice(-1), 16),
  trackId: id,
  albumId: '22222222-0000-0000-0000-000000000001',
  name,
  author: 'Artist',
  audioRef: `/api/stream/${id}`,
  imageRef: '/api/artwork/00000000-0000-0000-0000-000000000000',
  durationSeconds: 200,
});

const MY_DEVICE = 'aaaaaaaa-0000-0000-0000-000000000001';
const OTHER_DEVICE = 'bbbbbbbb-0000-0000-0000-000000000001';

describe('PlaybackSessionService — Phase 2 (corrected single-owner-of-audio)', () => {
  let originalWS: typeof WebSocket;
  let accessToken = signal<string | null>(null);
  let registered = signal(false);
  let currentMusic = signal<Music | null>(null);
  let currentPlayList = signal<Playlist | null>(null);
  let isPlaying = signal(false);
  let currentTime = signal(0);
  let audioSpy!: jasmine.SpyObj<AudioService>;
  let musicSpy!: jasmine.SpyObj<MusicService>;

  const flushMicrotasks = () =>
    new Promise<void>((res) => setTimeout(res, 0));

  beforeEach(() => {
    originalWS = (globalThis as any).WebSocket;
    (globalThis as any).WebSocket = MockWebSocket as unknown as typeof WebSocket;
    MockWebSocket.instances = [];

    accessToken = signal<string | null>(null);
    registered = signal(false);
    currentMusic = signal<Music | null>(null);
    currentPlayList = signal<Playlist | null>(null);
    isPlaying = signal(false);
    currentTime = signal(0);

    const authSpy = jasmine.createSpyObj(
      'AuthService',
      ['refresh'],
      { accessToken },
    );
    authSpy.refresh.and.returnValue(of('new-token'));

    audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['playToggle', 'play', 'pause', 'seekTo', 'loadMusic', 'cleanup'],
      { isPlaying, currentTime, duration: signal(200) },
    );

    musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'setCurrentPlaylist'],
      {
        currentMusic,
        currentPlayList,
        isLoading: signal(false),
      },
    );
    // selectMusic mutates currentMusic the way the real service does.
    musicSpy.selectMusic.and.callFake((m: Music) => currentMusic.set(m));

    const deviceSpy = jasmine.createSpyObj(
      'DeviceService',
      ['getOrCreateDeviceId'],
      { registered },
    );
    deviceSpy.getOrCreateDeviceId.and.returnValue(MY_DEVICE);

    TestBed.configureTestingModule({
      providers: [
        PlaybackSessionService,
        { provide: AuthService, useValue: authSpy },
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy },
        { provide: DeviceService, useValue: deviceSpy },
        { provide: DestroyRef, useValue: { onDestroy: () => {} } },
      ],
    });
  });

  afterEach(() => {
    (globalThis as any).WebSocket = originalWS;
  });

  const setupAndConnect = async () => {
    const service = TestBed.inject(PlaybackSessionService);
    TestBed.flushEffects();
    accessToken.set('jwt-token');
    registered.set(true);
    TestBed.flushEffects();
    await flushMicrotasks();
    return service;
  };

  const ws = () => MockWebSocket.instances[0];
  const sent = (type: string) =>
    ws()?.sentMessages.filter((m: any) => m?.type === type) ?? [];

  const pushSession = async (overrides: Record<string, unknown> = {}) => {
    const base = {
      userId: 'u',
      activeDeviceId: MY_DEVICE,
      trackId: '11111111-0000-0000-0000-000000000001',
      positionMs: 0,
      isPlaying: true,
      queue: ['11111111-0000-0000-0000-000000000001'],
      queueIndex: 0,
      updatedAt: '2026-05-23T00:00:00Z',
    };
    ws().onmessage?.({
      data: JSON.stringify({ type: 'session', session: { ...base, ...overrides } }),
    });
    TestBed.flushEffects();
    await flushMicrotasks();
  };

  // ── setQueue fires on user pick (only) ────────────────────────────────────

  it('sends setQueue with all playlist tracks when the user picks a track', async () => {
    await setupAndConnect();

    const tracks = [
      track('11111111-0000-0000-0000-000000000001', 'Alpha'),
      track('11111111-0000-0000-0000-000000000002', 'Bravo'),
      track('11111111-0000-0000-0000-000000000003', 'Charlie'),
    ];
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: tracks });
    currentMusic.set(tracks[1]);
    TestBed.flushEffects();

    const setQueueMsgs = sent('setQueue');
    expect(setQueueMsgs.length).toBeGreaterThanOrEqual(1);
    expect(setQueueMsgs[setQueueMsgs.length - 1]).toEqual(
      jasmine.objectContaining({
        type: 'setQueue',
        trackIds: tracks.map((t) => t.trackId),
        startIndex: 1,
      }),
    );
  });

  it('does NOT echo setQueue when the active-device effect mutates currentMusic from a remote session', async () => {
    // Simulate: the user picked nothing yet; the server tells us we're active
    // playing track Y (e.g. transferred from another device). The
    // active-device effect calls selectMusic(Y) under applyingRemote=true,
    // which must NOT trigger a new setQueue echo.
    const t = track('11111111-0000-0000-0000-000000000001');
    const playlist: Playlist = { id: 1, name: 'All', imageRef: '', musics: [t] };
    currentPlayList.set(playlist);
    await setupAndConnect();

    await pushSession({ trackId: t.trackId });

    expect(sent('setQueue').length).toBe(0);
    expect(musicSpy.selectMusic).toHaveBeenCalledWith(t);
  });

  it('does not emit the retired start message', async () => {
    await setupAndConnect();
    const t = track('22222222-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();
    expect(sent('start').length).toBe(0);
  });

  // ── Active device: single owner of audio load + play ─────────────────────

  it('loads + plays audio on the active device when the server broadcasts a track', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();

    await pushSession({ trackId: t.trackId, isPlaying: true });

    expect(audioSpy.loadMusic).toHaveBeenCalledWith(t.audioRef);
    expect(audioSpy.play).toHaveBeenCalled();
  });

  it('does NOT load audio on an inactive device even after the user picks a track', async () => {
    // The user clicked a track on this device, but the server keeps another
    // device as active. This used to play in parallel — the regression we
    // just fixed.
    const t = track('33333333-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();

    // Pretend another device is active.
    await pushSession({ activeDeviceId: OTHER_DEVICE, trackId: t.trackId });
    audioSpy.loadMusic.calls.reset();
    audioSpy.play.calls.reset();

    // User picks the same track locally.
    currentMusic.set(t);
    TestBed.flushEffects();
    await flushMicrotasks();

    expect(audioSpy.loadMusic).not.toHaveBeenCalled();
    expect(audioSpy.play).not.toHaveBeenCalled();
  });

  it('pauses local audio when this device loses active status', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();
    await pushSession({ trackId: t.trackId, isPlaying: true });
    isPlaying.set(true);
    audioSpy.pause.calls.reset();

    await pushSession({ activeDeviceId: OTHER_DEVICE, trackId: t.trackId });

    expect(audioSpy.pause).toHaveBeenCalled();
  });

  // ── Drift tolerance ──────────────────────────────────────────────────────

  it('does not re-seek when server position is within drift tolerance', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();
    await pushSession({ trackId: t.trackId, positionMs: 0 });
    audioSpy.seekTo.calls.reset();

    // Local audio is at 30s; server reports 30.5s. Drift < 1s.
    currentTime.set(30);
    await pushSession({ trackId: t.trackId, positionMs: 30_500 });

    expect(audioSpy.seekTo).not.toHaveBeenCalled();
  });

  it('re-seeks when drift exceeds the 1s tolerance', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();
    await pushSession({ trackId: t.trackId, positionMs: 0 });
    audioSpy.seekTo.calls.reset();

    currentTime.set(30);
    await pushSession({ trackId: t.trackId, positionMs: 60_000 });

    expect(audioSpy.seekTo).toHaveBeenCalledWith(60);
  });

  // ── Remote pause / resume drives local audio on the active device ────────

  it('pauses local audio when the server reports isPlaying=false on the same track', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();
    await pushSession({ trackId: t.trackId, isPlaying: true });
    isPlaying.set(true);
    audioSpy.pause.calls.reset();

    await pushSession({ trackId: t.trackId, isPlaying: false });

    expect(audioSpy.pause).toHaveBeenCalled();
  });

  it('resumes local audio when the server reports isPlaying=true on the same track', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();
    await pushSession({ trackId: t.trackId, isPlaying: false });
    isPlaying.set(false);
    audioSpy.play.calls.reset();

    await pushSession({ trackId: t.trackId, isPlaying: true });

    expect(audioSpy.play).toHaveBeenCalled();
  });

  // ── Session DTO carries queue + queueIndex (regression for Phase 1) ──────

  it('absorbs session messages with queue + queueIndex', async () => {
    const service = await setupAndConnect();
    ws().onmessage?.({
      data: JSON.stringify({
        type: 'session',
        session: {
          userId: 'u',
          activeDeviceId: 'd',
          trackId: 'tt',
          positionMs: 12000,
          isPlaying: true,
          queue: ['tt', 'tt2'],
          queueIndex: 0,
          updatedAt: '2026-05-23T00:00:00Z',
        },
      }),
    });

    expect(service.session()?.queue).toEqual(['tt', 'tt2']);
    expect(service.session()?.queueIndex).toBe(0);
  });
});
