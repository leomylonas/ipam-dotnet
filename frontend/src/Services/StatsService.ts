import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for subnet statistics returned by
 * GET /api/subnets/{subnetId}/stats. Mirrors SubnetStatsResponse.
 */
export const subnetStatsSchema = z.object({
	subnetId: z.uuid(),
	/** Total usable host IPs (excludes network address and broadcast). */
	totalIps: z.number(),
	/** IPs currently allocated. */
	allocatedCount: z.number(),
	/** IPs available for allocation (not allocated and not excluded). */
	freeCount: z.number(),
	/** IPs covered by exclusion rules. */
	excludedCount: z.number(),
});
export type SubnetStats = z.infer<typeof subnetStatsSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/**
 * Service class for subnet statistics API operations.
 * Statistics are computed live from the database on every request.
 */
export class StatsService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/subnets/{subnetId}/stats — returns a live utilisation snapshot
	 * for the specified subnet, including total, allocated, free, and excluded
	 * IP counts.
	 */
	getStats(subnetId: string): Promise<SubnetStats> {
		return this.client.getJson(`/api/subnets/${subnetId}/stats`, subnetStatsSchema);
	}
}

/**
 * Hook that returns a memoised StatsService backed by the singleton FetchClient.
 */
export function useStatsService(): StatsService {
	const fetchClient = useFetchClient();
	return useMemo(() => new StatsService(fetchClient), [fetchClient]);
}
