import { Component, effect, inject, input, signal } from '@angular/core';
import { ApiService } from '../core/api';
import { MediaType, PublicProfile, statusLabel, unitLabel } from '../core/models';
import { StatsView } from '../shared/stats-view';

@Component({
  selector: 'app-public-profile',
  imports: [StatsView],
  template: `
    @if (notFound()) {
      <p class="empty">Este perfil no existe o no es público.</p>
    } @else if (profile(); as p) {
      <h1>Lista de {{ p.profileName }}</h1>
      <div class="chips" role="group" aria-label="Tipo">
        <button type="button" [attr.aria-pressed]="type() === 'Anime'" (click)="type.set('Anime')">Anime</button>
        <button type="button" [attr.aria-pressed]="type() === 'Manga'" (click)="type.set('Manga')">Manga</button>
      </div>
      <app-stats-view [stats]="p.stats" />
      <h2>Su lista</h2>
      <div class="grid">
        @for (e of p.entries; track e.id) {
          <article class="card entry">
            @if (e.media.coverUrl) {
              <img [src]="e.media.coverUrl" alt="" loading="lazy" />
            }
            <div class="info">
              <strong>{{ e.media.title }}</strong>
              <span class="muted small">{{ label(e.status) }} · {{ unit() }} {{ e.progress }}/{{ e.media.total ?? '?' }}@if (e.score) { · ★{{ e.score }} }</span>
            </div>
          </article>
        }
      </div>
    }
  `,
  styles: `
    .entry img {
      width: 100%;
      aspect-ratio: 2 / 3;
      object-fit: cover;
      border-radius: 12px 12px 0 0;
      display: block;
    }
    .info {
      padding: 0.6rem;
      display: flex;
      flex-direction: column;
    }
    .small {
      font-size: 0.8rem;
    }
  `,
})
export class PublicProfilePage {
  private readonly api = inject(ApiService);
  readonly name = input.required<string>();
  protected readonly type = signal<MediaType>('Anime');
  protected readonly profile = signal<PublicProfile | null>(null);
  protected readonly notFound = signal(false);

  constructor() {
    effect(() => {
      this.api.publicProfile(this.name(), this.type()).subscribe({
        next: (p) => this.profile.set(p),
        error: () => this.notFound.set(true),
      });
    });
  }

  protected label = (s: Parameters<typeof statusLabel>[0]) => statusLabel(s, this.type());
  protected unit = () => unitLabel(this.type());
}
