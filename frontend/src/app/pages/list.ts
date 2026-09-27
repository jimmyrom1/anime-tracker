import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiService } from '../core/api';
import { Entry, ListStatus, MediaType, problemMessage, STATUSES, statusLabel, unitLabel } from '../core/models';
import { EntryEditor } from '../shared/entry-editor';

@Component({
  selector: 'app-list',
  imports: [RouterLink, EntryEditor],
  template: `
    <h1>Mi {{ mediaType() === 'Anime' ? 'anime' : 'manga' }}</h1>

    <div class="chips" role="group" aria-label="Filtrar por estado">
      <button type="button" [attr.aria-pressed]="filter() === null" (click)="filter.set(null)">Todos ({{ entries().length }})</button>
      @for (s of statuses; track s) {
        <button type="button" [attr.aria-pressed]="filter() === s" (click)="filter.set(s)">{{ label(s) }} ({{ counts()[s] }})</button>
      }
    </div>

    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }

    @if (loaded() && entries().length === 0) {
      <div class="empty">
        <p>Tu lista de {{ mediaType() === 'Anime' ? 'anime' : 'manga' }} está vacía.</p>
        <a class="button primary" routerLink="/buscar" [queryParams]="{ type: mediaType() }">Buscar y añadir</a>
      </div>
    }

    <div class="grid">
      @for (e of visible(); track e.id) {
        <article class="card entry">
          <button type="button" class="cover" (click)="editing.set(e)" [attr.aria-label]="'Editar ' + e.media.title">
            @if (e.media.coverUrl) {
              <img [src]="e.media.coverUrl" [alt]="" loading="lazy" />
            }
            @if (e.score) {
              <span class="score" title="Tu nota">★ {{ e.score }}</span>
            }
          </button>
          <div class="info">
            <strong class="title" [title]="e.media.title">{{ e.media.title }}</strong>
            <span class="muted small">
              {{ label(e.status) }}@if (e.platform) { · {{ e.platform }} }
            </span>
            <div class="progress" [attr.aria-label]="progressText(e)"><span [style.width.%]="percent(e)"></span></div>
            <div class="row">
              <span class="small">{{ progressText(e) }}</span>
              @if (canIncrement(e)) {
                <button type="button" class="plus" (click)="increment(e)" [disabled]="pending().has(e.id)" [attr.aria-label]="'Un ' + unit() + ' más de ' + e.media.title">+1</button>
              }
            </div>
          </div>
        </article>
      }
    </div>

    <app-entry-editor [entry]="editing()" (saved)="replace($event)" (removed)="drop($event)" (closed)="editing.set(null)" />
  `,
  styles: `
    .entry {
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
    .cover {
      position: relative;
      padding: 0;
      border: none;
      border-radius: 0;
      aspect-ratio: 2 / 3;
      background: var(--surface-2);
    }
    .cover img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }
    .score {
      position: absolute;
      top: 6px;
      right: 6px;
      background: rgb(0 0 0 / 0.75);
      color: #ffd66b;
      border-radius: 6px;
      padding: 0 6px;
      font-size: 0.8rem;
      font-weight: 700;
    }
    .info {
      padding: 0.6rem;
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
    }
    .title {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .small {
      font-size: 0.8rem;
    }
    .row {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .plus {
      padding: 0.1rem 0.6rem;
      font-weight: 700;
    }
  `,
})
export class ListPage {
  private readonly api = inject(ApiService);

  /** "anime" o "manga", de la URL /lista/:type. */
  readonly type = input.required<string>();
  protected readonly mediaType = computed<MediaType>(() => (this.type() === 'manga' ? 'Manga' : 'Anime'));

  protected readonly statuses = STATUSES;
  protected readonly entries = signal<Entry[]>([]);
  protected readonly loaded = signal(false);
  protected readonly filter = signal<ListStatus | null>(null);
  protected readonly editing = signal<Entry | null>(null);
  protected readonly error = signal<string | null>(null);
  /** Entradas con un +1 en curso: se desactiva el botón para no mandar dos por un doble toque. */
  protected readonly pending = signal(new Set<number>());

  protected readonly visible = computed(() => {
    const f = this.filter();
    return f ? this.entries().filter((e) => e.status === f) : this.entries();
  });
  protected readonly counts = computed(() => {
    const counts = Object.fromEntries(STATUSES.map((s) => [s, 0])) as Record<ListStatus, number>;
    for (const e of this.entries()) counts[e.status]++;
    return counts;
  });

  constructor() {
    effect(() => {
      const type = this.mediaType();
      this.loaded.set(false);
      this.filter.set(null);
      this.api.list(type).subscribe({
        next: (entries) => {
          this.entries.set(entries);
          this.loaded.set(true);
        },
        error: (e) => this.error.set(problemMessage(e)),
      });
    });
  }

  protected label = (s: ListStatus) => statusLabel(s, this.mediaType());
  protected unit = () => (this.mediaType() === 'Anime' ? 'episodio' : 'capítulo');

  protected progressText(e: Entry): string {
    return `${unitLabel(e.media.type)} ${e.progress} / ${e.media.total ?? '?'}`;
  }

  protected percent(e: Entry): number {
    if (e.status === 'Completed') return 100;
    return e.media.total ? Math.min(100, (e.progress / e.media.total) * 100) : 0;
  }

  protected canIncrement(e: Entry): boolean {
    return e.status !== 'Completed' && (e.media.total === null || e.progress < e.media.total);
  }

  protected increment(e: Entry) {
    this.pending.update((s) => new Set(s).add(e.id));
    this.api
      .increment(e.id)
      // finalize: el botón se vuelve a activar tanto si sale bien como si falla.
      .pipe(finalize(() => this.pending.update((s) => {
        const next = new Set(s);
        next.delete(e.id);
        return next;
      })))
      .subscribe({
        next: (updated) => this.replace(updated),
        error: (err) => this.error.set(problemMessage(err)),
      });
  }

  /**
   * Se actualiza en su sitio, sin mover la tarjeta: si subiera al principio, lo que queda bajo el
   * cursor sería el "+1" de otra serie y un doble toque sumaría un episodio a la que no es.
   * El orden por última actualización se aplica al volver a cargar la lista.
   */
  protected replace(updated: Entry) {
    this.entries.update((list) => list.map((x) => (x.id === updated.id ? updated : x)));
  }

  protected drop(id: number) {
    this.entries.update((list) => list.filter((x) => x.id !== id));
  }
}
