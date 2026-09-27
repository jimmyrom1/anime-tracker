import { Component, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, filter, Subject, switchMap, tap } from 'rxjs';
import { ApiService } from '../core/api';
import { ListStatus, MediaType, problemMessage, SearchResult, statusLabel } from '../core/models';

@Component({
  selector: 'app-search',
  imports: [FormsModule],
  template: `
    <h1>Añadir a tu lista</h1>
    <div class="chips" role="group" aria-label="Tipo">
      <button type="button" [attr.aria-pressed]="mediaType() === 'Anime'" (click)="setType('Anime')">Anime</button>
      <button type="button" [attr.aria-pressed]="mediaType() === 'Manga'" (click)="setType('Manga')">Manga</button>
    </div>
    <input
      type="search"
      [placeholder]="mediaType() === 'Anime' ? 'Busca un anime: Frieren, One Piece…' : 'Busca un manga: Chainsaw Man, Berserk…'"
      [ngModel]="query()"
      (ngModelChange)="onQuery($event)"
      aria-label="Buscar"
      autofocus
    />
    <p class="muted small">Catálogo de AniList. Se busca en japonés romanizado y en inglés.</p>

    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
    @if (searching()) {
      <p class="muted">Buscando…</p>
    }

    <ul class="results">
      @for (r of results(); track r.media.id) {
        <li class="card result">
          @if (r.media.coverUrl) {
            <img [src]="r.media.coverUrl" alt="" loading="lazy" />
          }
          <div class="body">
            <strong>{{ r.media.title }}</strong>
            @if (r.media.titleEnglish && r.media.titleEnglish !== r.media.title) {
              <span class="muted small">{{ r.media.titleEnglish }}</span>
            }
            <span class="muted small">
              {{ r.media.format ?? '' }} · {{ r.media.year ?? '¿?' }} ·
              {{ r.media.total ?? '?' }} {{ mediaType() === 'Anime' ? 'episodios' : 'capítulos' }}
              @if (r.media.releaseStatus === 'RELEASING') { · en emisión }
            </span>
            <span class="muted small">{{ r.media.genres.join(', ') }}</span>
          </div>
          <div class="add">
            @if (r.status) {
              <span class="in-list">✓ {{ label(r.status) }}</span>
            } @else {
              <select #status [attr.aria-label]="'Estado para ' + r.media.title">
                <option value="Planning">{{ label('Planning') }}</option>
                <option value="Current">{{ label('Current') }}</option>
                <option value="Completed">{{ label('Completed') }}</option>
              </select>
              <button type="button" class="primary" [disabled]="adding() === r.media.id" (click)="add(r, $any(status.value))">Añadir</button>
            }
          </div>
        </li>
      }
    </ul>
    @if (!searching() && query().trim().length >= 2 && results().length === 0 && !error()) {
      <p class="empty">No hay resultados para «{{ query() }}».</p>
    }
  `,
  styles: `
    .small {
      font-size: 0.8rem;
    }
    .results {
      list-style: none;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
    }
    .result {
      display: flex;
      gap: 0.8rem;
      padding: 0.6rem;
      align-items: center;
    }
    .result img {
      width: 56px;
      height: 80px;
      object-fit: cover;
      border-radius: 6px;
      flex-shrink: 0;
    }
    .body {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-width: 0;
    }
    .add {
      display: flex;
      gap: 0.4rem;
      align-items: center;
    }
    .add select {
      width: auto;
    }
    .in-list {
      color: var(--ok);
      white-space: nowrap;
    }
    @media (max-width: 560px) {
      .result {
        flex-wrap: wrap;
      }
      .add {
        width: 100%;
        justify-content: flex-end;
      }
    }
  `,
})
export class SearchPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly queries = new Subject<string>();

  /** ?type=Manga desde el enlace de la lista vacía. */
  readonly type = input<string>();
  protected readonly mediaType = signal<MediaType>('Anime');
  protected readonly query = signal('');
  protected readonly results = signal<SearchResult[]>([]);
  protected readonly searching = signal(false);
  protected readonly adding = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit() {
    if (this.type() === 'Manga') this.mediaType.set('Manga');
    // Se espera a que el usuario deje de escribir: una petición por búsqueda, no por tecla.
    this.queries
      .pipe(
        debounceTime(350),
        distinctUntilChanged(),
        filter((q) => q.trim().length >= 2),
        tap(() => {
          this.searching.set(true);
          this.error.set(null);
        }),
        // switchMap: si llega otra búsqueda, se descarta la anterior y no pisa los resultados.
        switchMap((q) => this.api.search(q.trim(), this.mediaType())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (results) => {
          this.results.set(results);
          this.searching.set(false);
        },
        error: (e) => {
          this.searching.set(false);
          this.error.set(problemMessage(e));
        },
      });
  }

  protected label = (s: ListStatus) => statusLabel(s, this.mediaType());

  protected onQuery(value: string) {
    this.query.set(value);
    this.queries.next(value);
  }

  protected setType(type: MediaType) {
    this.mediaType.set(type);
    this.results.set([]);
    if (this.query().trim().length >= 2) {
      this.searching.set(true);
      this.api.search(this.query().trim(), type).subscribe({
        next: (r) => {
          this.results.set(r);
          this.searching.set(false);
        },
        error: (e) => {
          this.searching.set(false);
          this.error.set(problemMessage(e));
        },
      });
    }
  }

  protected add(result: SearchResult, status: ListStatus) {
    this.adding.set(result.media.id);
    this.api.add(result.media.id, status).subscribe({
      next: (entry) => {
        this.adding.set(null);
        this.results.update((list) => list.map((r) => (r.media.id === result.media.id ? { ...r, entryId: entry.id, status: entry.status } : r)));
      },
      error: (e) => {
        this.adding.set(null);
        this.error.set(problemMessage(e));
      },
    });
  }
}
