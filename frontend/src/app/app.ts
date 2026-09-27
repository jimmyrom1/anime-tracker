import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/api';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a routerLink="/" class="brand">📺 Anime Tracker</a>
      @if (auth.isLoggedIn()) {
        <nav>
          <a routerLink="/lista/anime" routerLinkActive="active">Anime</a>
          <a routerLink="/lista/manga" routerLinkActive="active">Manga</a>
          <a routerLink="/buscar" routerLinkActive="active">Añadir</a>
          <a routerLink="/estadisticas" routerLinkActive="active">Estadísticas</a>
          <a routerLink="/ajustes" routerLinkActive="active">Perfil</a>
          <button type="button" (click)="auth.logout()">Salir</button>
        </nav>
      }
    </header>
    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    .topbar {
      position: sticky;
      top: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      gap: 1rem;
      flex-wrap: wrap;
      padding: 0.7rem 1rem;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
    }
    .brand {
      font-weight: 700;
      color: var(--text);
      margin-right: auto;
    }
    nav {
      display: flex;
      gap: 0.25rem;
      align-items: center;
      flex-wrap: wrap;
    }
    nav a {
      color: var(--muted);
      padding: 0.35rem 0.7rem;
      border-radius: 8px;
    }
    nav a.active {
      color: var(--text);
      background: var(--surface-2);
    }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
}
