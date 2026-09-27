import { DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { STATUSES, Stats, statusLabel } from '../core/models';

const MONTHS = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

/** Estadísticas de una lista; la usan tu página de estadísticas y el perfil público. */
@Component({
  selector: 'app-stats-view',
  imports: [DecimalPipe],
  template: `
    @let s = stats();
    <div class="tiles">
      <div class="card tile"><span class="muted">Obras</span><strong>{{ s.entries }}</strong></div>
      @if (s.type === 'Anime') {
        <div class="card tile"><span class="muted">Días vistos</span><strong>{{ s.daysWatched | number: '1.1-1' }}</strong></div>
        <div class="card tile"><span class="muted">Episodios</span><strong>{{ s.progressTotal | number }}</strong></div>
      } @else {
        <div class="card tile"><span class="muted">Capítulos leídos</span><strong>{{ s.progressTotal | number }}</strong></div>
      }
      <div class="card tile"><span class="muted">Nota media</span><strong>{{ s.meanScore === null ? '–' : (s.meanScore | number: '1.2-2') }}</strong></div>
    </div>

    <div class="columns">
      <section>
        <h2>Por estado</h2>
        @for (st of statuses; track st) {
          <div class="bar-row">
            <span>{{ label(st) }}</span>
            <div class="bar"><span [style.width.%]="share(s.byStatus[st], s.entries)"></span></div>
            <span class="num">{{ s.byStatus[st] }}</span>
          </div>
        }
      </section>

      <section>
        <h2>Tus notas</h2>
        <div class="histogram" role="img" [attr.aria-label]="'Distribución de notas del 1 al 10'">
          @for (count of s.scoreDistribution; track $index) {
            <div class="col">
              <span class="num small">{{ count || '' }}</span>
              <div class="stick" [style.height.%]="share(count, maxScoreCount())"></div>
              <span class="muted small">{{ $index + 1 }}</span>
            </div>
          }
        </div>
      </section>

      <section>
        <h2>Géneros favoritos</h2>
        @for (g of s.topGenres; track g.key) {
          <div class="bar-row">
            <span>{{ g.key }}</span>
            <div class="bar"><span [style.width.%]="share(g.count, s.topGenres[0].count)"></span></div>
            <span class="num">{{ g.count }}@if (g.meanScore) { <span class="muted"> · ★{{ g.meanScore | number: '1.1-1' }}</span> }</span>
          </div>
        } @empty {
          <p class="muted">Aún no hay datos.</p>
        }
      </section>

      <section>
        <h2>{{ s.type === 'Anime' ? 'Dónde lo ves' : 'Dónde lo lees' }}</h2>
        @for (p of s.platforms; track p.key) {
          <div class="bar-row">
            <span>{{ p.key }}</span>
            <div class="bar alt"><span [style.width.%]="share(p.count, s.platforms[0].count)"></span></div>
            <span class="num">{{ p.count }}</span>
          </div>
        } @empty {
          <p class="muted">Indica la plataforma al editar una entrada.</p>
        }
      </section>
    </div>

    <h2>{{ s.type === 'Anime' ? 'Episodios' : 'Capítulos' }} por mes</h2>
    <div class="histogram activity">
      @for (m of s.activity; track $index) {
        <div class="col">
          <span class="num small">{{ m.amount || '' }}</span>
          <div class="stick alt" [style.height.%]="share(m.amount, maxActivity())"></div>
          <span class="muted small">{{ month(m.month) }}</span>
        </div>
      }
    </div>
  `,
  styles: `
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 0.8rem;
    }
    .tile {
      padding: 0.9rem;
      display: flex;
      flex-direction: column;
    }
    .tile strong {
      font-size: 1.7rem;
    }
    .columns {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
      gap: 0 2rem;
    }
    .bar-row {
      display: grid;
      grid-template-columns: 9rem 1fr auto;
      gap: 0.6rem;
      align-items: center;
      margin-bottom: 0.45rem;
      font-size: 0.9rem;
    }
    .bar {
      height: 8px;
      background: var(--surface-2);
      border-radius: 4px;
      overflow: hidden;
    }
    .bar span {
      display: block;
      height: 100%;
      background: var(--accent);
    }
    .bar.alt span,
    .stick.alt {
      background: var(--accent-2);
    }
    .num {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .small {
      font-size: 0.75rem;
    }
    .histogram {
      display: flex;
      align-items: flex-end;
      gap: 6px;
      height: 160px;
    }
    .col {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: flex-end;
      height: 100%;
    }
    .stick {
      width: 100%;
      min-height: 2px;
      background: var(--accent);
      border-radius: 4px 4px 0 0;
    }
  `,
})
export class StatsView {
  readonly stats = input.required<Stats>();
  protected readonly statuses = STATUSES;
  protected readonly maxScoreCount = computed(() => Math.max(1, ...this.stats().scoreDistribution));
  protected readonly maxActivity = computed(() => Math.max(1, ...this.stats().activity.map((m) => m.amount)));

  protected label = (s: (typeof STATUSES)[number]) => statusLabel(s, this.stats().type);
  protected share = (value: number, total: number) => (total > 0 ? (value / total) * 100 : 0);
  protected month = (m: number) => MONTHS[m - 1];
}
