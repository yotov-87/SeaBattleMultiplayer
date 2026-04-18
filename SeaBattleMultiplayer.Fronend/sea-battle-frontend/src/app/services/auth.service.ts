import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthRequest, AuthResponse } from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly apiUrl = `${environment.apiUrl}/auth`;

  private _token    = signal<string | null>(localStorage.getItem('token'));
  private _username = signal<string | null>(localStorage.getItem('username'));
  private _userId   = signal<number | null>(this.parseUserIdFromStorage());

  readonly isLoggedIn = computed(() => !!this._token());
  readonly username   = computed(() => this._username());
  readonly userId     = computed(() => this._userId());

  constructor(private http: HttpClient) {}

  register(dto: AuthRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, dto).pipe(
      tap(res => this.saveSession(res))
    );
  }

  login(dto: AuthRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, dto).pipe(
      tap(res => this.saveSession(res))
    );
  }

  logout(): void {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    this._token.set(null);
    this._username.set(null);
    this._userId.set(null);
  }

  getToken(): string | null {
    return this._token();
  }

  private saveSession(res: AuthResponse): void {
    localStorage.setItem('token', res.token);
    localStorage.setItem('username', res.username);
    this._token.set(res.token);
    this._username.set(res.username);
    this._userId.set(AuthService.parseUserIdFromToken(res.token));
  }

  private parseUserIdFromStorage(): number | null {
    const token = localStorage.getItem('token');
    return token ? AuthService.parseUserIdFromToken(token) : null;
  }

  private static parseUserIdFromToken(token: string): number | null {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      // .NET Core maps ClaimTypes.NameIdentifier to this long URL in the JWT
      const raw =
        payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ??
        payload['sub'] ??
        payload['nameid'];
      const id = Number(raw);
      return isNaN(id) ? null : id;
    } catch {
      return null;
    }
  }
}
