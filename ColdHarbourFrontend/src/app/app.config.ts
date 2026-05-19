import { APP_INITIALIZER, ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { of } from 'rxjs';
import { switchMap } from 'rxjs/operators';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { DeviceService } from './features/devices/device.service';

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
