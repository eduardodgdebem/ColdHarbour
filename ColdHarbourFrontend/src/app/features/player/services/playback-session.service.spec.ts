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
  let duration = signal(200);
  let ended = signal(false);
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
    duration = signal(200);
    ended = signal(false);

    const authSpy = jasmine.createSpyObj(
      'AuthService',
      ['refresh'],
      { accessToken },
    );
    authSpy.refresh.and.returnValue(of('new-token'));

    audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['playToggle', 'play', 'pause', 'seekTo', 'loadMusic', 'cleanup'],
      { isPlaying, currentTime, duration, ended },
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
          repeatMode: 'off',
          shuffle: false,
          updatedAt: '2026-05-23T00:00:00Z',
        },
      }),
    });

    expect(service.session()?.queue).toEqual(['tt', 'tt2']);
    expect(service.session()?.queueIndex).toBe(0);
    expect(service.session()?.repeatMode).toBe('off');
    expect(service.session()?.shuffle).toBeFalse();
  });

  // ── Phase 3: shuffle / repeat / trackEnded ──────────────────────────────

  it('setRepeatMode sends a setRepeatMode message', async () => {
    const service = await setupAndConnect();
    service.setRepeatMode('all');
    expect(sent('setRepeatMode')[0]).toEqual(
      jasmine.objectContaining({ type: 'setRepeatMode', mode: 'all' }),
    );
  });

  it('setShuffle sends a setShuffle message', async () => {
    const service = await setupAndConnect();
    service.setShuffle(true);
    expect(sent('setShuffle')[0]).toEqual(
      jasmine.objectContaining({ type: 'setShuffle', enabled: true }),
    );
  });

  it('sends trackEnded only when this device is the active one', async () => {
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    await setupAndConnect();

    // Active device: ended fires → trackEnded sent.
    await pushSession({ trackId: t.trackId, activeDeviceId: MY_DEVICE });
    duration.set(180);
    ended.set(true);
    TestBed.flushEffects();
    await flushMicrotasks();
    expect(sent('trackEnded').length).toBe(1);
    expect(sent('trackEnded')[0]).toEqual(
      jasmine.objectContaining({
        type: 'trackEnded',
        trackId: t.trackId,
        durationMs: 180_000,
      }),
    );

    // Inactive device: ended is ignored.
    ended.set(false);
    await pushSession({ trackId: t.trackId, activeDeviceId: OTHER_DEVICE });
    ended.set(true);
    TestBed.flushEffects();
    await flushMicrotasks();
    expect(sent('trackEnded').length).toBe(1); // still 1
  });

  // ── Phase 4: queue mutations ───────────────────────────────────────────

  it('addToQueue sends an addToQueue message (append by default)', async () => {
    const service = await setupAndConnect();
    service.addToQueue('11111111-0000-0000-0000-00000000abcd');
    expect(sent('addToQueue').length).toBe(1);
    expect(sent('addToQueue')[0]).toEqual(
      jasmine.objectContaining({
        type: 'addToQueue',
        trackId: '11111111-0000-0000-0000-00000000abcd',
      }),
    );
    // No position in the payload when appending.
    expect((sent('addToQueue')[0] as Record<string, unknown>)['position']).toBeUndefined();
  });

  it('addToQueue with a position includes the position field', async () => {
    const service = await setupAndConnect();
    service.addToQueue('aaaa', 3);
    expect(sent('addToQueue')[0]).toEqual(
      jasmine.objectContaining({ trackId: 'aaaa', position: 3 }),
    );
  });

  it('removeFromQueue / reorderQueue / clearQueue all dispatch correctly', async () => {
    const service = await setupAndConnect();
    service.removeFromQueue(2);
    service.reorderQueue(0, 4);
    service.clearQueue();
    expect(sent('removeFromQueue')[0]).toEqual(
      jasmine.objectContaining({ type: 'removeFromQueue', index: 2 }),
    );
    expect(sent('reorderQueue')[0]).toEqual(
      jasmine.objectContaining({ type: 'reorderQueue', from: 0, to: 4 }),
    );
    expect(sent('clearQueue')[0]).toEqual(
      jasmine.objectContaining({ type: 'clearQueue' }),
    );
  });
});
