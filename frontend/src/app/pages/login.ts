import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { AuthService } from '../core/api';
import { problemMessage } from '../core/models';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <section class="card login">
      <h1>{{ mode() === 'login' ? 'Entrar' : 'Crear cuenta' }}</h1>
      <p class="muted">Lleva la cuenta de los animes y mangas que ves: por qué capítulo vas, dónde lo ves y qué nota le das.</p>
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
      <form (ngSubmit)="submit()">
        <label>Email <input name="email" type="email" autocomplete="email" [(ngModel)]="email" required /></label>
        <label>
          Contraseña
          <input name="password" type="password" [attr.autocomplete]="mode() === 'login' ? 'current-password' : 'new-password'" [(ngModel)]="password" required />
          @if (mode() === 'register') {
            <span class="muted">Mínimo 8 caracteres, con mayúscula, minúscula y número.</span>
          }
        </label>
        <button class="primary" type="submit" [disabled]="busy()">{{ mode() === 'login' ? 'Entrar' : 'Crear cuenta' }}</button>
      </form>
      <button type="button" class="link" (click)="toggle()">
        {{ mode() === 'login' ? '¿No tienes cuenta? Regístrate' : '¿Ya tienes cuenta? Entra' }}
      </button>
    </section>
  `,
  styles: `
    .login {
      max-width: 420px;
      margin: 3rem auto;
      padding: 1.5rem;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 0.9rem;
      margin: 1rem 0;
    }
    .link {
      border: none;
      background: none;
      color: var(--accent-2);
      padding: 0;
    }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly mode = signal<'login' | 'register'>('login');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected email = '';
  protected password = '';

  protected toggle() {
    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
    this.error.set(null);
  }

  protected submit() {
    this.busy.set(true);
    this.error.set(null);
    const login$ = this.auth.login(this.email, this.password);
    const flow$ = this.mode() === 'register' ? this.auth.register(this.email, this.password).pipe(switchMap(() => login$)) : login$;
    flow$.subscribe({
      next: () => this.router.navigateByUrl('/lista/anime'),
      error: (e) => {
        this.busy.set(false);
        // Identity devuelve 401 sin detalle si el login falla: se da un mensaje claro.
        this.error.set(e.status === 401 ? 'Email o contraseña incorrectos.' : problemMessage(e));
      },
    });
  }
}
