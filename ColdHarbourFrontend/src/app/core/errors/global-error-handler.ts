import { ErrorHandler, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

const MAX_MESSAGE_LENGTH = 200;
const DEFAULT_MESSAGE =
  "Something went wrong. The harbour didn't expect that signal.";

@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly router = inject(Router);

  handleError(error: unknown): void {
    console.error('[GlobalErrorHandler]', error);

    if (this.isHttpError(error)) {
      return;
    }

    const raw = this.extractMessage(error);
    const message = raw.slice(0, MAX_MESSAGE_LENGTH);

    this.router.navigate(['/error'], {
      queryParams: { code: 'UNEXPECTED', message },
      skipLocationChange: false,
    });
  }

  private isHttpError(error: unknown): boolean {
    if (error instanceof HttpErrorResponse) return true;
    const wrapped = (error as { rejection?: unknown } | null)?.rejection;
    return wrapped instanceof HttpErrorResponse;
  }

  private extractMessage(error: unknown): string {
    if (error instanceof Error && error.message) return error.message;
    if (typeof error === 'string' && error) return error;
    const maybe = (error as { message?: unknown } | null)?.message;
    if (typeof maybe === 'string' && maybe) return maybe;
    return DEFAULT_MESSAGE;
  }
}
