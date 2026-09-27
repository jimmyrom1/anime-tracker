import { Component, ElementRef, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api';
import { Entry, EntryUpdate, ListStatus, PLATFORM_SUGGESTIONS, problemMessage, STATUSES, statusLabel, unitLabel } from '../core/models';

/** Diálogo para editar una entrada: estado, progreso, nota, plataforma, fechas y notas. */
@Component({
  selector: 'app-entry-editor',
  imports: [FormsModule],
  template: `
    <dialog #dialog (close)="closed.emit()">
      @if (entry(); as e) {
        <h2>{{ e.media.title }}</h2>
        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
        <form class="form-grid" (ngSubmit)="save()">
          <label>
            Estado
            <select name="status" [(ngModel)]="form.status">
              @for (s of statuses; track s) {
                <option [value]="s">{{ label(s) }}</option>
              }
            </select>
          </label>
          <label>
            {{ unit() }} (de {{ e.media.total ?? '?' }})
            <input name="progress" type="number" min="0" [max]="e.media.total ?? 99999" [(ngModel)]="form.progress" />
          </label>
          <label>
            Nota
            <select name="score" [(ngModel)]="form.score">
              <option [ngValue]="null">Sin nota</option>
              @for (n of scores; track n) {
                <option [ngValue]="n">{{ n }}</option>
              }
            </select>
          </label>
          <label>
            {{ e.media.type === 'Anime' ? 'Dónde lo ves' : 'Dónde lo lees' }}
            <input name="platform" list="platforms" maxlength="40" [(ngModel)]="form.platform" placeholder="Crunchyroll, Netflix…" />
            <datalist id="platforms">
              @for (p of platforms(); track p) {
                <option [value]="p"></option>
              }
            </datalist>
          </label>
          <label>Empezado <input name="startedOn" type="date" [(ngModel)]="form.startedOn" /></label>
          <label>Terminado <input name="finishedOn" type="date" [(ngModel)]="form.finishedOn" /></label>
          <label class="wide">Notas <textarea name="notes" rows="3" maxlength="2000" [(ngModel)]="form.notes"></textarea></label>
          <div class="actions wide">
            <button type="button" class="danger" (click)="remove()">Quitar de la lista</button>
            @if (e.status === 'Completed') {
              <button type="button" (click)="rewatch()">{{ e.media.type === 'Anime' ? 'Volver a verla' : 'Volver a leerlo' }}</button>
            }
            <button type="button" (click)="dialog.close()">Cancelar</button>
            <button type="submit" class="primary" [disabled]="busy()">Guardar</button>
          </div>
        </form>
      }
    </dialog>
  `,
})
export class EntryEditor {
  private readonly api = inject(ApiService);

  readonly entry = input<Entry | null>(null);
  readonly saved = output<Entry>();
  readonly removed = output<number>();
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly statuses = STATUSES;
  protected readonly scores = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1];
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected form: EntryUpdate = { status: 'Planning', progress: 0, score: null, platform: null, notes: null, startedOn: null, finishedOn: null };

  constructor() {
    // Cada vez que se elige una entrada, se copia al formulario y se abre el diálogo.
    effect(() => {
      const e = this.entry();
      if (!e) return;
      this.form = {
        status: e.status, progress: e.progress, score: e.score, platform: e.platform,
        notes: e.notes, startedOn: e.startedOn, finishedOn: e.finishedOn,
      };
      this.error.set(null);
      this.dialog().nativeElement.showModal();
    });
  }

  protected label = (s: ListStatus) => statusLabel(s, this.entry()?.media.type ?? 'Anime');
  protected unit = () => (this.entry()?.media.type === 'Anime' ? 'Episodio' : 'Capítulo');
  protected platforms = () => PLATFORM_SUGGESTIONS[this.entry()?.media.type ?? 'Anime'];
  protected readonly unitLabel = unitLabel;

  protected save() {
    const e = this.entry();
    if (!e) return;
    this.run(this.api.update(e.id, { ...this.form, startedOn: this.form.startedOn || null, finishedOn: this.form.finishedOn || null }), (saved) => this.saved.emit(saved));
  }

  protected rewatch() {
    const e = this.entry();
    if (e) this.run(this.api.rewatch(e.id), (saved) => this.saved.emit(saved));
  }

  protected remove() {
    const e = this.entry();
    if (!e || !confirm(`¿Quitar «${e.media.title}» de tu lista?`)) return;
    this.run(this.api.remove(e.id), () => this.removed.emit(e.id));
  }

  private run<T>(request: import('rxjs').Observable<T>, done: (value: T) => void) {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
        this.dialog().nativeElement.close();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(problemMessage(err));
      },
    });
  }
}
