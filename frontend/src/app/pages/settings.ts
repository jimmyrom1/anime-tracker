import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api';
import { Me, problemMessage } from '../core/models';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>Perfil</h1>
    @if (me(); as m) {
      <section class="card box">
        <p class="muted">Sesión iniciada como <strong>{{ m.email }}</strong></p>
        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
        @if (saved()) {
          <p class="ok" role="status">Guardado.</p>
        }
        <form (ngSubmit)="save()">
          <label>
            Nombre público
            <input name="profileName" [(ngModel)]="profileName" placeholder="p. ej. jose_otaku" maxlength="30" />
            <span class="muted">De 3 a 30 caracteres: minúsculas, números, - y _.</span>
          </label>
          <label class="check">
            <input type="checkbox" name="isPublic" [(ngModel)]="isPublic" />
            Cualquiera con el enlace puede ver mi lista y mis estadísticas (las notas personales nunca se muestran)
          </label>
          <button class="primary" type="submit" [disabled]="busy()">Guardar</button>
        </form>
        @if (m.isProfilePublic && m.profileName) {
          <p>Tu perfil público: <a [routerLink]="['/u', m.profileName]">/u/{{ m.profileName }}</a></p>
        }
      </section>
    }
  `,
  styles: `
    .box {
      padding: 1.2rem;
      max-width: 560px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 0.9rem;
      align-items: flex-start;
    }
    form label:first-child {
      width: 100%;
    }
    .check {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
    }
    .ok {
      color: var(--ok);
    }
  `,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly me = signal<Me | null>(null);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected profileName = '';
  protected isPublic = false;

  ngOnInit() {
    this.api.me().subscribe((m) => this.apply(m));
  }

  protected save() {
    this.busy.set(true);
    this.saved.set(false);
    this.error.set(null);
    this.api.updateProfile(this.profileName.trim() || null, this.isPublic).subscribe({
      next: (m) => {
        this.apply(m);
        this.busy.set(false);
        this.saved.set(true);
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(problemMessage(e));
      },
    });
  }

  private apply(m: Me) {
    this.me.set(m);
    this.profileName = m.profileName ?? '';
    this.isPublic = m.isProfilePublic;
  }
}
