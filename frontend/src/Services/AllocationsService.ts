import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for a single allocation as returned by the API.
 * Mirrors the AllocationResponse DTO.
 */
export const allocationSchema = z.object({
	id: z.uuid(),
	/** Allocated IP address in dotted-decimal notation. */
	ipAddress: z.string(),
	/** ASP.NET Identity user ID of the allocating user. */
	userId: z.string(),
	subnetId: z.uuid(),
	description: z.string(),
	/** UTC timestamp when the allocation was made — ISO-8601 string. */
	allocatedAt: z.string(),
	/** Shared across all IPs in a bulk request; null for single allocations. */
	bulkId: z.uuid().nullable(),
});
export type Allocation = z.infer<typeof allocationSchema>;

/**
 * Validation schema for POST /api/allocations. The system picks the next
 * available IP; the caller only specifies the subnet and a description.
 */
export const allocateSchema = z.object({
	subnetId: z.uuid('A subnet must be selected'),
	description: z.string().min(1, 'Description is required'),
});
export type AllocateRequest = z.infer<typeof allocateSchema>;

/**
 * Validation schema for POST /api/allocations/bulk. Requests N consecutive
 * IPs from the specified subnet.
 */
export const bulkAllocateSchema = z.object({
	subnetId: z.uuid('A subnet must be selected'),
	count: z.coerce.number().int().min(1, 'Count must be at least 1').max(256, 'Count may not exceed 256'),
	description: z.string().min(1, 'Description is required'),
});
export type BulkAllocateRequest = z.infer<typeof bulkAllocateSchema>;

/**
 * Schema for the IP availability check response from
 * GET /api/subnets/{subnetId}/check/{ip}.
 */
export const checkIpResponseSchema = z.object({
	ip: z.string(),
	available: z.boolean(),
});
export type CheckIpResponse = z.infer<typeof checkIpResponseSchema>;

/**
 * Query parameter schema for filtering allocations by tag.
 * Both fields are optional — supplying neither returns all allocations.
 */
export const allocationFilterSchema = z.object({
	tagKey: z.string().optional(),
	tagValue: z.string().optional(),
});
export type AllocationFilter = z.infer<typeof allocationFilterSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of allocations. */
const allocationListSchema = z.array(allocationSchema);

/**
 * Service class for IP allocation API operations.
 * Handles single allocations, bulk allocations, IP availability checks,
 * and releasing allocations.
 */
export class AllocationsService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/allocations — returns allocations visible to the caller.
	 * Supports optional tag-based filtering via query parameters.
	 * GlobalAdmin sees all; TenantAdmin/User see their tenancy's allocations.
	 */
	list(filter?: AllocationFilter): Promise<Allocation[]> {
		// Build query string from the filter object, omitting empty values.
		const params = new URLSearchParams();
		if (filter?.tagKey) {
			params.set('tagKey', filter.tagKey);
		}
		if (filter?.tagValue) {
			params.set('tagValue', filter.tagValue);
		}
		const qs = params.toString();
		const url = qs ? `/api/allocations?${qs}` : '/api/allocations';
		return this.client.getJson(url, allocationListSchema);
	}

	/**
	 * POST /api/allocations — requests the next available IP from the specified
	 * subnet. The system selects the IP; the caller only provides a subnet and
	 * description.
	 */
	allocate(request: AllocateRequest): Promise<Allocation> {
		return this.client.postJson('/api/allocations', request, allocationSchema);
	}

	/**
	 * POST /api/allocations/bulk — requests N consecutive IPs from the specified
	 * subnet. Returns 409 if no contiguous block of the requested size exists.
	 * All IPs in the bulk share a BulkId but are individually releasable.
	 */
	bulkAllocate(request: BulkAllocateRequest): Promise<Allocation[]> {
		return this.client.postJson('/api/allocations/bulk', request, allocationListSchema);
	}

	/**
	 * GET /api/subnets/{subnetId}/check/{ip} — checks whether a specific IP
	 * is currently available for allocation.
	 */
	checkIp(subnetId: string, ip: string): Promise<CheckIpResponse> {
		return this.client.getJson(`/api/subnets/${subnetId}/check/${ip}`, checkIpResponseSchema);
	}

	/**
	 * DELETE /api/allocations/{id} — releases (deallocates) the specified IP.
	 * An audit record is written atomically with the release.
	 */
	release(id: string): Promise<void> {
		return this.client.deleteJson(`/api/allocations/${id}`);
	}
}

/**
 * Hook that returns a memoised AllocationsService backed by the singleton FetchClient.
 */
export function useAllocationsService(): AllocationsService {
	const fetchClient = useFetchClient();
	return useMemo(() => new AllocationsService(fetchClient), [fetchClient]);
}
