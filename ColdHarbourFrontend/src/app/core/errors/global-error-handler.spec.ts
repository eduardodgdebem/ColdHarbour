import { TestBed } from '@angular/core/testing';
import { ErrorHandler } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { GlobalErrorHandler } from './global-error-handler';

describe('GlobalErrorHandler', () => {
  let handler: GlobalErrorHandler;
  let routerNavigateSpy: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'error', children: [] }]),
        { provide: ErrorHandler, useClass: GlobalErrorHandler },
      ],
    });

    const router = TestBed.inject(Router);
    routerNavigateSpy = spyOn(router, 'navigate').and.returnValue(
      Promise.resolve(true),
    );

    handler = TestBed.inject(ErrorHandler) as GlobalErrorHandler;
    spyOn(console, 'error');
  });

  it('navigates to /error with UNEXPECTED code on plain Error', () => {
    handler.handleError(new Error('boom'));
    expect(routerNavigateSpy).toHaveBeenCalledWith(
      ['/error'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({
          code: 'UNEXPECTED',
          message: jasmine.stringMatching(/boom/),
        }),
      }),
    );
  });

  it('logs uncaught errors to the console', () => {
    const err = new Error('signal lost');
    handler.handleError(err);
    expect(console.error).toHaveBeenCalled();
  });

  it('ignores HttpErrorResponse — interceptor handles those', () => {
    const httpErr = new HttpErrorResponse({
      status: 500,
      statusText: 'Server Error',
    });
    handler.handleError(httpErr);
    expect(routerNavigateSpy).not.toHaveBeenCalled();
  });

  it('also ignores errors wrapped in rejection with HttpErrorResponse', () => {
    const httpErr = new HttpErrorResponse({ status: 503 });
    const wrapped = { rejection: httpErr } as unknown as Error;
    handler.handleError(wrapped);
    expect(routerNavigateSpy).not.toHaveBeenCalled();
  });

  it('truncates very long error messages before passing to the URL', () => {
    const longMessage = 'x'.repeat(500);
    handler.handleError(new Error(longMessage));
    const call = routerNavigateSpy.calls.mostRecent();
    const params = call.args[1].queryParams as { message: string };
    expect(params.message.length).toBeLessThanOrEqual(200);
  });

  it('falls back to a default message when error has no message', () => {
    handler.handleError({} as Error);
    expect(routerNavigateSpy).toHaveBeenCalledWith(
      ['/error'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({
          code: 'UNEXPECTED',
        }),
      }),
    );
  });
});
