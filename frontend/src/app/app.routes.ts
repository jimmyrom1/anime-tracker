import { Routes } from '@angular/router';
import { authGuard } from './core/api';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'lista/anime' },
  { path: 'login', title: 'Entrar · Anime Tracker', loadComponent: () => import('./pages/login').then((m) => m.LoginPage) },
  {
    path: 'lista/:type',
    title: 'Mi lista · Anime Tracker',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/list').then((m) => m.ListPage),
  },
  { path: 'buscar', title: 'Añadir · Anime Tracker', canActivate: [authGuard], loadComponent: () => import('./pages/search').then((m) => m.SearchPage) },
  {
    path: 'estadisticas',
    title: 'Estadísticas · Anime Tracker',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/stats').then((m) => m.StatsPage),
  },
  { path: 'ajustes', title: 'Perfil · Anime Tracker', canActivate: [authGuard], loadComponent: () => import('./pages/settings').then((m) => m.SettingsPage) },
  // Perfil público: no necesita sesión.
  { path: 'u/:name', title: 'Perfil · Anime Tracker', loadComponent: () => import('./pages/public-profile').then((m) => m.PublicProfilePage) },
  { path: '**', redirectTo: '' },
];
