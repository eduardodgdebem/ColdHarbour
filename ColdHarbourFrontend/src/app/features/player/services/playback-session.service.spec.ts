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

describe('PlaybackSessionService — Phase 2 server-driven transport', () => {
  let originalWS: typeof WebSocket;
  let accessToken = signal<string | null>(null);
  let registered = signal(false);
  let currentMusic = signal<Music | null>(null);
  let currentPlayList = signal<Playlist | null>(null);
  let isPlaying = signal(false);
  let currentTime = signal(0);
  let audioSpy!: jasmine.SpyObj<AudioService>;

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
      ['playToggle', 'seekTo', 'loadMusic', 'cleanup'],
      { isPlaying, currentTime, duration: signal(200) },
    );

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'setCurrentPlaylist'],
      {
        currentMusic,
        currentPlayList,
        isLoading: signal(false),
      },
    );

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

  const pushSession = (overrides: Record<string, unknown> = {}) => {
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
  };

  // ── setQueue still fires when the user picks a track ─────────────────────

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

  it('does not emit the retired start message', async () => {
    await setupAndConnect();

    const t = track('22222222-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();

    expect(sent('start').length).toBe(0);
  });

  // ── Transport methods are thin WS wrappers ───────────────────────────────

  it('next() sends a next message with this deviceId', async () => {
    const service = await setupAndConnect();
    service.next();
    expect(sent('next').length).toBe(1);
    expect(sent('next')[0]).toEqual(
      jasmine.objectContaining({ type: 'next', deviceId: MY_DEVICE }),
    );
  });

  it('previous() sends a previous message', async () => {
    const service = await setupAndConnect();
    service.previous();
    expect(sent('previous')[0]).toEqual(
      jasmine.objectContaining({ type: 'previous', deviceId: MY_DEVICE }),
    );
  });

  it('seek(ms) sends a seek with floored non-negative positionMs', async () => {
    const service = await setupAndConnect();
    service.seek(12_345.7);
    expect(sent('seek')[0]).toEqual(
      jasmine.objectContaining({
        type: 'seek',
        deviceId: MY_DEVICE,
        positionMs: 12_345,
      }),
    );
    service.seek(-50);
    expect(sent('seek')[1]).toEqual(
      jasmine.objectContaining({ positionMs: 0 }),
    );
  });

  it('pause() and resume() only send messages — they never touch local audio directly', async () => {
    const service = await setupAndConnect();
    service.pause();
    service.resume();
    expect(sent('pause').length).toBe(1);
    expect(sent('resume').length).toBe(1);
    expect(audioSpy.playToggle).not.toHaveBeenCalled();
  });

  // ── Drift tolerance: do not seek on tiny server-vs-local diff ────────────

  it('does not re-seek when server position is within drift tolerance', async () => {
    await setupAndConnect();
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();
    // First session push lands us as active on track tt; pendingActivation
    // triggers a seek to 0 which we don't care about here.
    pushSession({ trackId: t.trackId, positionMs: 0 });
    await flushMicrotasks();
    audioSpy.seekTo.calls.reset();

    // Local audio sits at 30s; server reports 30.5s. Drift is 500ms < 1000ms.
    currentTime.set(30);
    pushSession({ trackId: t.trackId, positionMs: 30_500 });
    await flushMicrotasks();

    expect(audioSpy.seekTo).not.toHaveBeenCalled();
  });

  it('does re-seek when drift exceeds the 1s tolerance', async () => {
    await setupAndConnect();
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();
    pushSession({ trackId: t.trackId, positionMs: 0 });
    await flushMicrotasks();
    audioSpy.seekTo.calls.reset();

    currentTime.set(30);
    pushSession({ trackId: t.trackId, positionMs: 60_000 });
    await flushMicrotasks();

    expect(audioSpy.seekTo).toHaveBeenCalledWith(60);
  });

  // ── Remote pause / resume drives local audio on the active device ────────

  it('toggles local audio when remote pause arrives for the same track', async () => {
    await setupAndConnect();
    const t = track('11111111-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();
    pushSession({ trackId: t.trackId, isPlaying: true });
    await flushMicrotasks();
    isPlaying.set(true);
    audioSpy.playToggle.calls.reset();

    pushSession({ trackId: t.trackId, isPlaying: false });
    await flushMicrotasks();

    expect(audioSpy.playToggle).toHaveBeenCalled();
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
