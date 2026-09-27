import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpParams } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, Observable, tap, throwError } from 'rxjs';
import { Entry, EntryUpdate, ListStatus, Me, MediaType, PublicProfile, SearchResult, Stats } from './models';

const TOKEN_KEY = 'anime-tracker-token';

/** El almacenamiento puede no estar disponible (modo privado): la sesión dura lo que la pestaña. */
const storage = {
  get: () => {
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  },
  set: (value: string | null) => {
    try {
      if (value) localStorage.setItem(TOKEN_KEY, value);
      else localStorage.removeItem(TOKEN_KEY);
    } catch {
      /* sin almacenamiento */
    }
  },
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly token = signal<string | null>(storage.get());
  readonly isLoggedIn = computed(() => this.token() !== null);

  register(email: string, password: string) {
    return this.http.post('/api/auth/register', { email, password });
  }

  login(email: string, password: string) {
    return this.http.post<{ accessToken: string }>('/api/auth/login', { email, password }).pipe(
      tap((res) => {
        storage.set(res.accessToken);
        this.token.set(res.accessToken);
      }),
    );
  }

  logout() {
    storage.set(null);
    this.token.set(null);
    this.router.navigateByUrl('/login');
  }
}

/** Añade el token a las peticiones a /api y, si caduca (401), manda a iniciar sesión. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const request = token && req.url.startsWith('/api/') ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && token) auth.logout();
      return throwError(() => error);
    }),
  );
};

export const authGuard: CanActivateFn = () => (inject(AuthService).isLoggedIn() ? true : inject(Router).parseUrl('/login'));

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  search(q: string, type: MediaType): Observable<SearchResult[]> {
    return this.http.get<SearchResult[]>('/api/catalog/search', { params: { q, type } });
  }

  list(type: MediaType, status?: ListStatus): Observable<Entry[]> {
    let params = new HttpParams().set('type', type);
    if (status) params = params.set('status', status);
    return this.http.get<Entry[]>('/api/list', { params });
  }

  add(mediaId: number, status: ListStatus): Observable<Entry> {
    return this.http.post<Entry>('/api/list', { mediaId, status });
  }

  update(id: number, body: EntryUpdate): Observable<Entry> {
    return this.http.put<Entry>(`/api/list/${id}`, body);
  }

  increment(id: number): Observable<Entry> {
    return this.http.post<Entry>(`/api/list/${id}/increment`, null);
  }

  rewatch(id: number): Observable<Entry> {
    return this.http.post<Entry>(`/api/list/${id}/rewatch`, null);
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`/api/list/${id}`);
  }

  stats(type: MediaType): Observable<Stats> {
    return this.http.get<Stats>('/api/stats', { params: { type } });
  }

  me(): Observable<Me> {
    return this.http.get<Me>('/api/me');
  }

  updateProfile(profileName: string | null, isProfilePublic: boolean): Observable<Me> {
    return this.http.put<Me>('/api/me/profile', { profileName, isProfilePublic });
  }

  publicProfile(name: string, type: MediaType): Observable<PublicProfile> {
    return this.http.get<PublicProfile>(`/api/profiles/${encodeURIComponent(name)}`, { params: { type } });
  }
}
