export interface ProblemDetails {
  title: string;
  status: number;
  detail: string;
  errors?: Record<string, string[]>;
}

export interface AppError {
  friendlyMessage: string;
  status: number;
  detail: string;
  raw: ProblemDetails;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    value !== null &&
    typeof value === 'object' &&
    typeof (value as Record<string, unknown>)['title'] === 'string' &&
    typeof (value as Record<string, unknown>)['status'] === 'number'
  );
}

export function isAppError(value: unknown): value is AppError {
  return (
    value !== null &&
    typeof value === 'object' &&
    typeof (value as Record<string, unknown>)['friendlyMessage'] === 'string' &&
    typeof (value as Record<string, unknown>)['status'] === 'number'
  );
}
