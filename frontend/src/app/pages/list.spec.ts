import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Entry } from '../core/models';
import { ListPage } from './list';

const entry = (id: number, title: string, progress: number, total: number | null, status: Entry['status'] = 'Current'): Entry => ({
  id,
  status,
  progress,
  score: null,
  platform: id === 1 ? 'Crunchyroll' : null,
  notes: null,
  startedOn: null,
  finishedOn: null,
  repeatCount: 0,
  updatedAt: '2026-09-27T18:00:00Z',
  media: {
    id: id * 100, type: 'Anime', title, titleEnglish: null, coverUrl: null, total, episodes: total, chapters: null,
    durationMinutes: 24, format: 'TV', releaseStatus: 'FINISHED', year: 2024, genres: [], averageScore: null,
  },
});

describe('ListPage', () => {
  async function setup(entries: Entry[]) {
    TestBed.configureTestingModule({
      imports: [ListPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ListPage);
    fixture.componentRef.setInput('type', 'anime');
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/list' && r.params.get('type') === 'Anime').flush(entries);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  const titles = (el: HTMLElement) => [...el.querySelectorAll('.title')].map((t) => t.textContent?.trim());

  it('shows progress, platform and counts per status', async () => {
    const { el } = await setup([entry(1, 'Frieren', 5, 10), entry(2, 'Dandadan', 12, 12, 'Completed')]);

    expect(titles(el)).toEqual(['Frieren', 'Dandadan']);
    expect(el.textContent).toContain('Ep. 5 / 10');
    expect(el.textContent).toContain('Viendo · Crunchyroll');
    expect(el.textContent).toContain('Completado (1)');
    // Una serie terminada no tiene botón +1.
    expect(el.querySelectorAll('.plus').length).toBe(1);
  });

  it('+1 updates the card in place, so a double tap never hits another show', async () => {
    const { fixture, http, el } = await setup([entry(1, 'Frieren', 5, 10), entry(2, 'One Piece', 1100, null)]);

    const plusOnePiece = el.querySelectorAll<HTMLButtonElement>('.plus')[1];
    plusOnePiece.click();
    fixture.detectChanges();
    expect(plusOnePiece.disabled).toBe(true); // sin segundo envío mientras llega la respuesta

    http.expectOne('/api/list/2/increment').flush(entry(2, 'One Piece', 1101, null));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(titles(el)).toEqual(['Frieren', 'One Piece']);
    expect(el.textContent).toContain('Ep. 1101 / ?');
    expect(el.querySelectorAll<HTMLButtonElement>('.plus')[1].disabled).toBe(false);
  });

  it('shows the API error and re-enables the button if +1 fails', async () => {
    const { fixture, http, el } = await setup([entry(1, 'Frieren', 9, 10)]);

    el.querySelector<HTMLButtonElement>('.plus')!.click();
    http.expectOne('/api/list/1/increment').flush(
      { title: 'Datos no válidos', errors: { progress: ['Solo tiene 10.'] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('.alert')?.textContent).toContain('Solo tiene 10.');
    expect(el.querySelector<HTMLButtonElement>('.plus')!.disabled).toBe(false);
  });
});
