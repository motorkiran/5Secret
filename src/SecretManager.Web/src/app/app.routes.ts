import { Routes } from '@angular/router';
import {
	authenticatedGuard,
	bootstrapOnlyGuard,
	landingRedirectGuard,
	loginOnlyGuard
} from './core/guards/route-guards';
import { RedirectPlaceholderComponent } from './shared/redirect-placeholder.component';

export const routes: Routes = [
	{
		path: '',
		pathMatch: 'full',
		canActivate: [landingRedirectGuard],
		component: RedirectPlaceholderComponent
	},
	{
		path: 'bootstrap',
		canActivate: [bootstrapOnlyGuard],
		loadComponent: () =>
			import('./features/bootstrap/bootstrap-page.component').then(
				(m) => m.BootstrapPageComponent
			)
	},
	{
		path: 'login',
		canActivate: [loginOnlyGuard],
		loadComponent: () =>
			import('./features/login/login-page.component').then((m) => m.LoginPageComponent)
	},
	{
		path: 'app',
		canActivate: [authenticatedGuard],
		loadComponent: () =>
			import('./features/shell/shell-layout.component').then((m) => m.ShellLayoutComponent),
		children: [
			{
				path: '',
				pathMatch: 'full',
				redirectTo: 'topology'
			},
			{
				path: 'topology',
				loadComponent: () =>
					import('./features/topology/topology-page.component').then((m) => m.TopologyPageComponent)
			},
			{
				path: 'catalog',
				loadComponent: () =>
					import('./features/catalog/catalog-page.component').then((m) => m.CatalogPageComponent)
			},
			{
				path: 'workflow',
				loadComponent: () =>
					import('./features/workflow/workflow-page.component').then((m) => m.WorkflowPageComponent)
			},
			{
				path: 'runtime',
				loadComponent: () =>
					import('./features/runtime/runtime-page.component').then((m) => m.RuntimePageComponent)
			}
		]
	},
	{
		path: '**',
		redirectTo: ''
	}
];
