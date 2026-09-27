import { problemMessage, statusLabel } from './models';

describe('statusLabel', () => {
  it('speaks about watching anime and reading manga', () => {
    expect(statusLabel('Current', 'Anime')).toBe('Viendo');
    expect(statusLabel('Current', 'Manga')).toBe('Leyendo');
    expect(statusLabel('Planning', 'Manga')).toBe('Pendiente de leer');
  });
});

describe('problemMessage', () => {
  it('prefers the field error of an ASP.NET Core ProblemDetails', () => {
    const error = { error: { title: 'Datos no válidos', errors: { score: ['La puntuación va de 1 a 10.'] } } };
    expect(problemMessage(error)).toBe('La puntuación va de 1 a 10.');
  });

  it('falls back to the title, the message and finally a generic text', () => {
    expect(problemMessage({ error: { title: 'Ya la tienes en tu lista.' } })).toBe('Ya la tienes en tu lista.');
    expect(problemMessage({ error: { message: 'Ese nombre ya está cogido.' } })).toBe('Ese nombre ya está cogido.');
    expect(problemMessage(new Error('boom'), 'Sin conexión')).toBe('Sin conexión');
  });
});
