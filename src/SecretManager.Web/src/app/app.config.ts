import {
  APP_INITIALIZER,
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { SessionStore } from './core/state/session.store';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(),
    {
      provide: APP_INITIALIZER,
      multi: true,
      deps: [SessionStore],
      useFactory: (sessionStore: SessionStore) => () => sessionStore.initialize()
    },
    provideRouter(routes)
  ]
};
