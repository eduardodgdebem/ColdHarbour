import { HttpClient } from '@angular/common/http';
import { Injectable, signal, computed } from '@angular/core';
import { Observable, of } from 'rxjs';
import { tap, map, catchError } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _accessToken = signal<string | null>(null);
  private readonly _userId = signal<string | null>(null);
  private readonly _email = signal<string | null>(null);
  private refreshSchedule?: ReturnType<typeof setTimeout>;

  readonly isAuthenticated = computed(() => this._accessToken() !== null);
  readonly accessToken = this._accessToken.asReadonly();
  readonly email = this._email.asReadonly();

  constructor(private http: HttpClient) {}

  login(email: string, password: string): Observable<void> {
    const deviceId = this.getOrCreateDeviceId();
    return this.http.post<{ accessToken: string; userId: string; email: string }>(
      `${environment.apiBase}/auth/login`,
      { email, password, deviceId },
      { withCredentials: true }
    ).pipe(
      tap(res => this.storeTokens(res.accessToken, res.userId, res.email)),
      map(() => void 0)
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>(
      `${environment.apiBase}/auth/logout`,
      {},
      {
        withCredentials: true,
        headers: { Authorization: `Bearer ${this._accessToken()}` }
      }
    ).pipe(
      tap(() => this.clearTokens()),
      map(() => void 0),
      catchError(() => { this.clearTokens(); return of(void 0); })
    );
  }

  refresh(): Observable<string> {
    const deviceId = this.getOrCreateDeviceId();
    return this.http.post<{ accessToken: string }>(
      `${environment.apiBase}/auth/refresh`,
      { deviceId },
      { withCredentials: true }
    ).pipe(
      tap(res => {
        this._accessToken.set(res.accessToken);
        this.scheduleRefresh(res.accessToken);
      }),
      map(res => res.accessToken)
    );
  }

  tryRestoreSession(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => of(false))
    );
  }

  private storeTokens(accessToken: string, userId: string, email: string): void {
    this._accessToken.set(accessToken);
    this._userId.set(userId);
    this._email.set(email);
    this.scheduleRefresh(accessToken);
  }

  private clearTokens(): void {
    this._accessToken.set(null);
    this._userId.set(null);
    this._email.set(null);
    if (this.refreshSchedule) clearTimeout(this.refreshSchedule);
  }

  private scheduleRefresh(token: string): void {
    if (this.refreshSchedule) clearTimeout(this.refreshSchedule);
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const expiresMs = payload.exp * 1000;
      const refreshAt = expiresMs - Date.now() - 60_000;
      if (refreshAt > 0) {
        this.refreshSchedule = setTimeout(() => this.refresh().subscribe(), refreshAt);
      }
    } catch { /* malformed token — ignore */ }
  }

  private getOrCreateDeviceId(): string {
    let id = localStorage.getItem('deviceId');
    if (!id) {
      id = this.generateUUID();
      localStorage.setItem('deviceId', id);
    }
    return id;
  }

  private generateUUID(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = Math.random() * 16 | 0;
      return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
  }
}
