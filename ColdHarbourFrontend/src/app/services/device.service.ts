import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class DeviceService {
  constructor(private http: HttpClient) {}

  register(): Observable<void> {
    const deviceId = this.getOrCreateDeviceId();
    const supportedCodecs = this.probeCodecs();
    const preferredProfile = supportedCodecs.includes('opus') ? 'opus-128' : 'mp3-192';

    return this.http.post<void>(`${environment.apiBase}/devices`, {
      deviceId,
      name: navigator.userAgent.slice(0, 128),
      supportedCodecs,
      preferredProfile,
      bitrateCap: null,
    }).pipe(catchError(() => of(void 0)));
  }

  getOrCreateDeviceId(): string {
    let id = localStorage.getItem('deviceId');
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem('deviceId', id);
    }
    return id;
  }

  private probeCodecs(): string[] {
    const audio = new Audio();
    const candidates: { codec: string; mime: string }[] = [
      { codec: 'mp3', mime: 'audio/mpeg' },
      { codec: 'flac', mime: 'audio/flac' },
      { codec: 'm4a', mime: 'audio/mp4; codecs="mp4a.40.2"' },
      { codec: 'ogg', mime: 'audio/ogg; codecs="vorbis"' },
      { codec: 'opus', mime: 'audio/webm; codecs="opus"' },
      { codec: 'wav', mime: 'audio/wav' },
    ];
    return candidates
      .filter(c => audio.canPlayType(c.mime) !== '')
      .map(c => c.codec);
  }
}
