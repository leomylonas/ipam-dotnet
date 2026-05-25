import { useQuery } from '@tanstack/react-query';
import { useDashboardService } from '../Services/DashboardService';

/** Stable query keys for the three dashboard endpoints. */
export const dashboardKeys = {
	global: ['dashboard', 'global'] as const,
	tenant: ['dashboard', 'tenant'] as const,
	user: ['dashboard', 'user'] as const,
};

/**
 * Query hook for GET /dashboard/global. Returns system-wide statistics for
 * GlobalAdmin. The query is disabled for non-GlobalAdmin callers to prevent
 * unnecessary 403 errors — the component is responsible for calling the
 * correct hook based on the authenticated role.
 */
export function useGlobalDashboardQuery(enabled: boolean) {
	const service = useDashboardService();
	return useQuery({
		queryKey: dashboardKeys.global,
		queryFn: () => service.getGlobal(),
		enabled,
	});
}

/**
 * Query hook for GET /dashboard/tenant. Returns tenancy-scoped statistics
 * for TenantAdmin.
 */
export function useTenantDashboardQuery(enabled: boolean) {
	const service = useDashboardService();
	return useQuery({
		queryKey: dashboardKeys.tenant,
		queryFn: () => service.getTenant(),
		enabled,
	});
}

/**
 * Query hook for GET /dashboard/user. Returns accessible subnets and recent
 * allocations for TenantUser.
 */
export function useUserDashboardQuery(enabled: boolean) {
	const service = useDashboardService();
	return useQuery({
		queryKey: dashboardKeys.user,
		queryFn: () => service.getUser(),
		enabled,
	});
}

/**
 * Composite hook that returns all dashboard-related hooks as a single object,
 * covering all three role-specific dashboard endpoints.
 * Use this when a component needs access to multiple dashboard queries without
 * importing each hook individually.
 */
export function useDashboard() {
	return {
		useGlobal: useGlobalDashboardQuery,
		useTenant: useTenantDashboardQuery,
		useUser: useUserDashboardQuery,
	};
}
