import { APP_INITIALIZER, ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { of } from 'rxjs';
import { switchMap } from 'rxjs/operators';

import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth.interceptor';
import { AuthService } from './services/auth.service';
import { DeviceService } from './services/device.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    {
      provide: APP_INITIALIZER,
      useFactory: (auth: AuthService, device: DeviceService) => () =>
        auth.tryRestoreSession().pipe(
          switchMap(ok => ok ? device.register() : of(void 0))
        ),
      deps: [AuthService, DeviceService],
      multi: true
    }
  ]
};
