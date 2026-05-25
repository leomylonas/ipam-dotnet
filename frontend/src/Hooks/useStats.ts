import { useQuery } from '@tanstack/react-query';
import { useStatsService } from '../Services/StatsService';

/** Query key factory for subnet stats. */
export const statsKeys = {
	subnet: (subnetId: string) => ['stats', subnetId] as const,
};

/**
 * Query hook for GET /api/subnets/{subnetId}/stats. Returns a live utilisation
 * snapshot for the specified subnet.
 */
export function useSubnetStatsQuery(subnetId: string) {
	const service = useStatsService();
	return useQuery({
		queryKey: statsKeys.subnet(subnetId),
		queryFn: () => service.getStats(subnetId),
		enabled: !!subnetId,
	});
}

/**
 * Composite hook that returns all stats-related hooks as a single object.
 * Use this when a component needs access to stats operations without importing
 * each hook individually.
 */
export function useStats() {
	return {
		useSubnetStats: useSubnetStatsQuery,
	};
}
