export type MediaType = 'Anime' | 'Manga';
export type ListStatus = 'Current' | 'Completed' | 'Paused' | 'Dropped' | 'Planning';

export const STATUSES: ListStatus[] = ['Current', 'Completed', 'Paused', 'Dropped', 'Planning'];

/** Los textos cambian según sea anime (ver) o manga (leer). */
export function statusLabel(status: ListStatus, type: MediaType): string {
  const anime = type === 'Anime';
  switch (status) {
    case 'Current':
      return anime ? 'Viendo' : 'Leyendo';
    case 'Completed':
      return 'Completado';
    case 'Paused':
      return 'En pausa';
    case 'Dropped':
      return 'Abandonado';
    case 'Planning':
      return anime ? 'Pendiente de ver' : 'Pendiente de leer';
  }
}

export const unitLabel = (type: MediaType) => (type === 'Anime' ? 'Ep.' : 'Cap.');

/** Sugerencias para el campo "dónde lo ves"; se puede escribir cualquier otra. */
export const PLATFORM_SUGGESTIONS: Record<MediaType, string[]> = {
  Anime: ['Crunchyroll', 'Netflix', 'Prime Video', 'Disney+', 'HIDIVE', 'ADN', 'Max', 'YouTube', 'Blu-ray / DVD'],
  Manga: ['Manga Plus', 'Tomo físico', 'Kindle', 'Shonen Jump+', 'Webtoon', 'Comikey'],
};

export interface Media {
  id: number;
  type: MediaType;
  title: string;
  titleEnglish: string | null;
  coverUrl: string | null;
  total: number | null;
  episodes: number | null;
  chapters: number | null;
  durationMinutes: number | null;
  format: string | null;
  releaseStatus: string | null;
  year: number | null;
  genres: string[];
  averageScore: number | null;
}

export interface Entry {
  id: number;
  media: Media;
  status: ListStatus;
  progress: number;
  score: number | null;
  platform: string | null;
  notes: string | null;
  startedOn: string | null;
  finishedOn: string | null;
  repeatCount: number;
  updatedAt: string;
}

export interface EntryUpdate {
  status: ListStatus;
  progress: number;
  score: number | null;
  platform: string | null;
  notes: string | null;
  startedOn: string | null;
  finishedOn: string | null;
}

export interface SearchResult {
  media: Media;
  entryId: number | null;
  status: ListStatus | null;
}

export interface CountByKey {
  key: string;
  count: number;
  meanScore: number | null;
}

export interface Stats {
  type: MediaType;
  entries: number;
  byStatus: Record<ListStatus, number>;
  progressTotal: number;
  daysWatched: number;
  meanScore: number | null;
  scoreDistribution: number[];
  topGenres: CountByKey[];
  platforms: CountByKey[];
  activity: { year: number; month: number; amount: number }[];
}

export interface Me {
  email: string;
  profileName: string | null;
  isProfilePublic: boolean;
}

export interface PublicProfile {
  profileName: string;
  entries: Entry[];
  stats: Stats;
}

/** ProblemDetails de ASP.NET Core: `errors` trae los mensajes por campo (422/400). */
export interface Problem {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
  message?: string;
}

export function problemMessage(error: unknown, fallback = 'Algo ha fallado. Inténtalo de nuevo.'): string {
  const body = (error as { error?: Problem })?.error;
  const firstFieldError = body?.errors ? Object.values(body.errors).flat()[0] : undefined;
  return firstFieldError ?? body?.message ?? body?.title ?? fallback;
}
