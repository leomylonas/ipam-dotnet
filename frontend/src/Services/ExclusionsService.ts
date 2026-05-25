import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Schemas ───────────────────────────────────────────────────────────────────

/** Simple IPv4 address pattern used for exclusion start/end validation. */
const ipv4Pattern = /^\d{1,3}(\.\d{1,3}){3}$/;

/**
 * Zod schema for an exclusion range as returned by the API.
 * Mirrors the ExclusionResponse DTO.
 */
export const exclusionSchema = z.object({
	id: z.uuid(),
	subnetId: z.uuid(),
	/** First IP in the excluded range (inclusive). */
	start: z.string(),
	/** Last IP in the excluded range (inclusive). Equal to start for single-IP exclusions. */
	end: z.string(),
	description: z.string(),
});
export type Exclusion = z.infer<typeof exclusionSchema>;

/**
 * Validation schema for POST /api/subnets/{subnetId}/exclusions.
 * Both start and end must be valid IPv4 addresses.
 */
export const createExclusionSchema = z.object({
	start: z.string().min(1, 'Start IP is required').regex(ipv4Pattern, 'Must be a valid IPv4 address'),
	end: z.string().min(1, 'End IP is required').regex(ipv4Pattern, 'Must be a valid IPv4 address'),
	description: z.string().min(1, 'Description is required'),
});
export type CreateExclusionRequest = z.infer<typeof createExclusionSchema>;

/**
 * Validation schema for PUT /api/subnets/{subnetId}/exclusions/{id}.
 * Only the description is editable; range bounds are immutable.
 */
export const updateExclusionSchema = z.object({
	description: z.string().min(1, 'Description is required'),
});
export type UpdateExclusionRequest = z.infer<typeof updateExclusionSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of exclusions. */
const exclusionListSchema = z.array(exclusionSchema);

/**
 * Service class for exclusion range API operations.
 * All exclusion endpoints are nested under /api/subnets/{subnetId}/exclusions.
 */
export class ExclusionsService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/subnets/{subnetId}/exclusions — returns all exclusion ranges
	 * for the specified subnet.
	 */
	list(subnetId: string): Promise<Exclusion[]> {
		return this.client.getJson(`/api/subnets/${subnetId}/exclusions`, exclusionListSchema);
	}

	/**
	 * POST /api/subnets/{subnetId}/exclusions — adds a new exclusion range.
	 * For a single-IP exclusion, set start and end to the same address.
	 */
	create(subnetId: string, request: CreateExclusionRequest): Promise<Exclusion> {
		return this.client.postJson(`/api/subnets/${subnetId}/exclusions`, request, exclusionSchema);
	}

	/**
	 * PUT /api/subnets/{subnetId}/exclusions/{id} — updates an exclusion's
	 * description. The IP range bounds are immutable.
	 */
	update(subnetId: string, id: string, request: UpdateExclusionRequest): Promise<Exclusion> {
		return this.client.putJson(`/api/subnets/${subnetId}/exclusions/${id}`, request, exclusionSchema);
	}

	/**
	 * DELETE /api/subnets/{subnetId}/exclusions/{id} — removes an exclusion range.
	 */
	delete(subnetId: string, id: string): Promise<void> {
		return this.client.deleteJson(`/api/subnets/${subnetId}/exclusions/${id}`);
	}
}

/**
 * Hook that returns a memoised ExclusionsService backed by the singleton FetchClient.
 */
export function useExclusionsService(): ExclusionsService {
	const fetchClient = useFetchClient();
	return useMemo(() => new ExclusionsService(fetchClient), [fetchClient]);
}
