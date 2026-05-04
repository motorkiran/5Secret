import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionStore } from '../state/session.store';

export const landingRedirectGuard: CanActivateFn = (_, state) => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);
  const target = sessionStore.routeForCurrentState();
  return target === state.url ? true : router.createUrlTree([target]);
};

export const bootstrapOnlyGuard: CanActivateFn = () => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  if (!sessionStore.isInitialized()) {
    return true;
  }

  return router.createUrlTree([sessionStore.isAuthenticated() ? '/app' : '/login']);
};

export const loginOnlyGuard: CanActivateFn = () => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  if (!sessionStore.isInitialized()) {
    return router.createUrlTree(['/bootstrap']);
  }

  return sessionStore.isAuthenticated() ? router.createUrlTree(['/app']) : true;
};

export const authenticatedGuard: CanActivateFn = () => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  if (!sessionStore.isInitialized()) {
    return router.createUrlTree(['/bootstrap']);
  }

  return sessionStore.isAuthenticated() ? true : router.createUrlTree(['/login']);
};