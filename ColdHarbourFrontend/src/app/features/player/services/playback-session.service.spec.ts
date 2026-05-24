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

describe('PlaybackSessionService — Phase 1 server-side queue', () => {
  let originalWS: typeof WebSocket;
  let accessToken = signal<string | null>(null);
  let registered = signal(false);
  let currentMusic = signal<Music | null>(null);
  let currentPlayList = signal<Playlist | null>(null);
  let isPlaying = signal(false);
  let currentTime = signal(0);

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

    const audioSpy = jasmine.createSpyObj(
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
    deviceSpy.getOrCreateDeviceId.and.returnValue('device-A');

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

  const sent = (type: string) =>
    MockWebSocket.instances[0]?.sentMessages.filter(
      (m: any) => m?.type === type,
    ) ?? [];

  it('sends setQueue with all playlist tracks when the user picks a track', async () => {
    await setupAndConnect();

    const tracks = [
      track('11111111-0000-0000-0000-000000000001', 'Alpha'),
      track('11111111-0000-0000-0000-000000000002', 'Bravo'),
      track('11111111-0000-0000-0000-000000000003', 'Charlie'),
    ];
    currentPlayList.set({
      id: 1,
      name: 'All',
      imageRef: '',
      musics: tracks,
    });
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

  it('updates setQueue when the user advances to a different track in the same playlist', async () => {
    await setupAndConnect();

    const tracks = [
      track('22222222-0000-0000-0000-000000000001'),
      track('22222222-0000-0000-0000-000000000002'),
    ];
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: tracks });
    currentMusic.set(tracks[0]);
    TestBed.flushEffects();
    currentMusic.set(tracks[1]);
    TestBed.flushEffects();

    const setQueueMsgs = sent('setQueue') as Array<{ startIndex: number }>;
    expect(setQueueMsgs.length).toBeGreaterThanOrEqual(2);
    expect(setQueueMsgs[setQueueMsgs.length - 1].startIndex).toBe(1);
  });

  it('does not send setQueue before a playlist has loaded', async () => {
    await setupAndConnect();

    currentMusic.set(track('33333333-0000-0000-0000-000000000001'));
    TestBed.flushEffects();

    expect(sent('setQueue').length).toBe(0);
  });

  it('still emits start alongside setQueue (Phase 1 keeps backward compat)', async () => {
    await setupAndConnect();

    const t = track('44444444-0000-0000-0000-000000000001');
    currentPlayList.set({ id: 1, name: 'All', imageRef: '', musics: [t] });
    currentMusic.set(t);
    TestBed.flushEffects();

    expect(sent('start').length).toBeGreaterThanOrEqual(1);
    expect(sent('setQueue').length).toBeGreaterThanOrEqual(1);
  });

  it('absorbs session messages with the new queue + queueIndex fields', async () => {
    const service = await setupAndConnect();
    const ws = MockWebSocket.instances[0];

    ws.onmessage?.({
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
