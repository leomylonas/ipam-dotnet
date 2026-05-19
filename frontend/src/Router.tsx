import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router';
import { authStore } from './Stores/AuthStore';
import { AppShell } from './Components/AppShell';
import { LoginPage } from './Pages/LoginPage';
import { DashboardPage } from './Pages/DashboardPage';
import { NotFoundPage } from './Pages/NotFoundPage';

/**
 * Root route — wraps the entire application. A bare Outlet so the root itself
 * adds no visual chrome; layout is handled by the login page and the app shell.
 */
const rootRoute = createRootRoute({
	component: () => <Outlet />,
	notFoundComponent: NotFoundPage,
});

/** Login route — publicly accessible, no auth check. */
const loginRoute = createRoute({
	getParentRoute: () => rootRoute,
	path: '/login',
	component: LoginPage,
});

/**
 * App route — the auth-guarded layout parent for all protected pages. Reads
 * directly from the authStore singleton (not router context) to avoid a
 * React render-cycle race where context updates lag behind navigation.
 */
const appRoute = createRoute({
	getParentRoute: () => rootRoute,
	id: '_app',
	beforeLoad: () => {
		// Redirect to login if there is no authenticated user in the store.
		// The check runs synchronously before any component renders, so protected
		// pages are never briefly flashed to an unauthenticated user.
		if (!authStore.isAuthenticated()) {
			throw redirect({ to: '/login' });
		}
	},
	component: AppShell,
});

/** Dashboard — the default landing page for all authenticated roles. */
const dashboardRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/',
	component: DashboardPage,
});

const routeTree = rootRoute.addChildren([loginRoute, appRoute.addChildren([dashboardRoute])]);

/** The application router. No context is needed — auth state lives in the store. */
export const router = createRouter({ routeTree });

// Augment TanStack Router's module so that useNavigate, Link, etc. are
// typed against this router's route tree throughout the project.
declare module '@tanstack/react-router' {
	interface Register {
		router: typeof router;
	}
}
