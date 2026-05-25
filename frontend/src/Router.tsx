import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router';
import { authStore } from './Stores/AuthStore';
import { AppShell } from './Components/AppShell';
import { LoginPage } from './Pages/LoginPage';
import { DashboardPage } from './Pages/DashboardPage';
import { TenanciesPage } from './Pages/TenanciesPage';
import { UsersPage } from './Pages/UsersPage';
import { SharedSubnetsPage } from './Pages/SharedSubnetsPage';
import { SharedSubnetDetailPage } from './Pages/SharedSubnetDetailPage';
import { SubnetsPage } from './Pages/SubnetsPage';
import { SubnetDetailPage } from './Pages/SubnetDetailPage';
import { AllocationsPage } from './Pages/AllocationsPage';
import { AuditPage } from './Pages/AuditPage';
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
		// The check runs synchronously before any component renders so protected
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

/**
 * Tenancies — GlobalAdmin only. Redirects to dashboard for other roles to
 * prevent the page from rendering even briefly before a navigation occurs.
 */
const tenanciesRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/tenancies',
	beforeLoad: () => {
		if (!authStore.isGlobalAdmin()) {
			throw redirect({ to: '/' });
		}
	},
	component: TenanciesPage,
});

/**
 * Users — GlobalAdmin and TenantAdmin. TenantUser is redirected home because
 * they cannot manage other users (only their own password via the API).
 */
const usersRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/users',
	beforeLoad: () => {
		if (authStore.isTenantUser()) {
			throw redirect({ to: '/' });
		}
	},
	component: UsersPage,
});

/** Shared subnets — GlobalAdmin only. */
const sharedSubnetsRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/shared-subnets',
	beforeLoad: () => {
		if (!authStore.isGlobalAdmin()) {
			throw redirect({ to: '/' });
		}
	},
	component: SharedSubnetsPage,
});

/** Shared subnet detail — GlobalAdmin only. */
const sharedSubnetDetailRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/shared-subnets/$subnetId',
	beforeLoad: () => {
		if (!authStore.isGlobalAdmin()) {
			throw redirect({ to: '/' });
		}
	},
	component: SharedSubnetDetailPage,
});

/**
 * Subnets — GlobalAdmin and TenantAdmin. TenantUser cannot manage subnets.
 */
const subnetsRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/subnets',
	beforeLoad: () => {
		if (authStore.isTenantUser()) {
			throw redirect({ to: '/' });
		}
	},
	component: SubnetsPage,
});

/**
 * Subnet detail — GlobalAdmin and TenantAdmin. Nested under /subnets so
 * the breadcrumb in the AppSideNav highlights the Subnets nav item.
 */
const subnetDetailRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/subnets/$subnetId',
	beforeLoad: () => {
		if (authStore.isTenantUser()) {
			throw redirect({ to: '/' });
		}
	},
	component: SubnetDetailPage,
});

/** Allocations — all authenticated roles can access this page. */
const allocationsRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/allocations',
	component: AllocationsPage,
});

/**
 * Audit log — GlobalAdmin and TenantAdmin. TenantUser has no visibility into
 * audit log entries.
 */
const auditRoute = createRoute({
	getParentRoute: () => appRoute,
	path: '/audit',
	beforeLoad: () => {
		if (authStore.isTenantUser()) {
			throw redirect({ to: '/' });
		}
	},
	component: AuditPage,
});

const routeTree = rootRoute.addChildren([
	loginRoute,
	appRoute.addChildren([
		dashboardRoute,
		tenanciesRoute,
		usersRoute,
		sharedSubnetsRoute,
		sharedSubnetDetailRoute,
		subnetsRoute,
		subnetDetailRoute,
		allocationsRoute,
		auditRoute,
	]),
]);

/** The application router. No context is needed — auth state lives in the store. */
export const router = createRouter({ routeTree });

// Augment TanStack Router's module so that useNavigate, Link, etc. are
// typed against this router's route tree throughout the project.
declare module '@tanstack/react-router' {
	interface Register {
		router: typeof router;
	}
}
