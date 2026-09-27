import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../core/api';
import { MediaType, problemMessage, Stats } from '../core/models';
import { StatsView } from '../shared/stats-view';

@Component({
  selector: 'app-stats',
  imports: [StatsView],
  template: `
    <h1>Estadísticas</h1>
    <div class="chips" role="group" aria-label="Tipo">
      <button type="button" [attr.aria-pressed]="type() === 'Anime'" (click)="type.set('Anime')">Anime</button>
      <button type="button" [attr.aria-pressed]="type() === 'Manga'" (click)="type.set('Manga')">Manga</button>
    </div>
    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
    @if (stats(); as s) {
      <app-stats-view [stats]="s" />
    }
  `,
})
export class StatsPage {
  private readonly api = inject(ApiService);
  protected readonly type = signal<MediaType>('Anime');
  protected readonly stats = signal<Stats | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.stats(this.type()).subscribe({
        next: (s) => this.stats.set(s),
        error: (e) => this.error.set(problemMessage(e)),
      });
    });
  }
}
