import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';
import { tenancySchema, type Tenancy } from './TenanciesService';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for a subnet as returned by the API. Covers both shared and
 * private subnets — the `type` field distinguishes them.
 */
export const subnetSchema = z.object({
	id: z.uuid(),
	/** CIDR notation, e.g. "192.168.1.0/24". */
	cidr: z.string(),
	name: z.string(),
	description: z.string(),
	/** Either "Shared" or "Private". */
	type: z.enum(['Shared', 'Private']),
	/** Null for Shared subnets; set for Private subnets. */
	tenancyId: z.uuid().nullable(),
	createdAt: z.string(),
});
export type Subnet = z.infer<typeof subnetSchema>;

/**
 * Validation schema for creating a subnet (both shared and private).
 * The `type` and `tenancyId` are inferred from the endpoint, not the body.
 */
export const createSubnetSchema = z.object({
	cidr: z
		.string()
		.min(1, 'CIDR is required')
		.regex(/^\d{1,3}(\.\d{1,3}){3}\/\d{1,2}$/, 'Must be valid CIDR notation (e.g. 192.168.1.0/24)'),
	name: z.string().min(1, 'Name is required'),
	description: z.string().min(1, 'Description is required'),
});
export type CreateSubnetRequest = z.infer<typeof createSubnetSchema>;

/**
 * Validation schema for updating a subnet. CIDR is immutable after creation,
 * so only name and description can be changed.
 */
export const updateSubnetSchema = z.object({
	name: z.string().min(1, 'Name is required'),
	description: z.string().min(1, 'Description is required'),
});
export type UpdateSubnetRequest = z.infer<typeof updateSubnetSchema>;

/**
 * Request body for POST /api/subnets/shared/{id}/access. Restricts a shared
 * subnet so only the specified tenancy can allocate from it.
 */
export const grantSubnetAccessSchema = z.object({
	tenancyId: z.uuid('A valid tenancy must be selected'),
});
export type GrantSubnetAccessRequest = z.infer<typeof grantSubnetAccessSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of subnets. */
const subnetListSchema = z.array(subnetSchema);

/**
 * Service class for shared and private subnet API operations.
 * Shared subnet endpoints are under /api/subnets/shared; private subnets
 * are nested under /api/tenancies/{tenancyId}/subnets.
 */
export class SubnetsService {
	constructor(private readonly client: FetchClient) {}

	// ── Shared subnets ────────────────────────────────────────────────────────

	/**
	 * GET /api/subnets/shared — returns shared subnets accessible to the caller's
	 * tenancy. GlobalAdmin receives all shared subnets.
	 */
	listShared(): Promise<Subnet[]> {
		return this.client.getJson('/api/subnets/shared', subnetListSchema);
	}

	/**
	 * POST /api/subnets/shared — creates a new shared subnet. GlobalAdmin only.
	 */
	createShared(request: CreateSubnetRequest): Promise<Subnet> {
		return this.client.postJson('/api/subnets/shared', request, subnetSchema);
	}

	/**
	 * PUT /api/subnets/shared/{id} — updates a shared subnet's name and description.
	 */
	updateShared(id: string, request: UpdateSubnetRequest): Promise<Subnet> {
		return this.client.putJson(`/api/subnets/shared/${id}`, request, subnetSchema);
	}

	/**
	 * DELETE /api/subnets/shared/{id} — deletes a shared subnet.
	 */
	deleteShared(id: string): Promise<void> {
		return this.client.deleteJson(`/api/subnets/shared/${id}`);
	}

	/**
	 * GET /api/subnets/shared/{id}/access — lists tenancies explicitly granted
	 * access. An empty array means no tenancy has access.
	 */
	listAccess(subnetId: string): Promise<Tenancy[]> {
		return this.client.getJson(`/api/subnets/shared/${subnetId}/access`, z.array(tenancySchema));
	}

	/**
	 * POST /api/subnets/shared/{id}/access — grants a specific tenancy access
	 * to a shared subnet.
	 */
	grantAccess(subnetId: string, request: GrantSubnetAccessRequest): Promise<void> {
		return this.client.postJson(`/api/subnets/shared/${subnetId}/access`, request);
	}

	/**
	 * DELETE /api/subnets/shared/{id}/access/{tenancyId} — removes a tenancy
	 * restriction from a shared subnet.
	 */
	revokeAccess(subnetId: string, tenancyId: string): Promise<void> {
		return this.client.deleteJson(`/api/subnets/shared/${subnetId}/access/${tenancyId}`);
	}

	// ── Private subnets ───────────────────────────────────────────────────────

	/**
	 * GET /api/tenancies/{tenancyId}/subnets — returns private subnets for the
	 * specified tenancy.
	 */
	listPrivate(tenancyId: string): Promise<Subnet[]> {
		return this.client.getJson(`/api/tenancies/${tenancyId}/subnets`, subnetListSchema);
	}

	/**
	 * POST /api/tenancies/{tenancyId}/subnets — creates a new private subnet.
	 * The CIDR must fall within RFC1918 ranges.
	 */
	createPrivate(tenancyId: string, request: CreateSubnetRequest): Promise<Subnet> {
		return this.client.postJson(`/api/tenancies/${tenancyId}/subnets`, request, subnetSchema);
	}

	/**
	 * PUT /api/tenancies/{tenancyId}/subnets/{subnetId} — updates a private
	 * subnet's name and description.
	 */
	updatePrivate(tenancyId: string, subnetId: string, request: UpdateSubnetRequest): Promise<Subnet> {
		return this.client.putJson(`/api/tenancies/${tenancyId}/subnets/${subnetId}`, request, subnetSchema);
	}

	/**
	 * DELETE /api/tenancies/{tenancyId}/subnets/{subnetId} — deletes a private subnet.
	 */
	deletePrivate(tenancyId: string, subnetId: string): Promise<void> {
		return this.client.deleteJson(`/api/tenancies/${tenancyId}/subnets/${subnetId}`);
	}
}

/**
 * Hook that returns a memoised SubnetsService backed by the singleton FetchClient.
 */
export function useSubnetsService(): SubnetsService {
	const fetchClient = useFetchClient();
	return useMemo(() => new SubnetsService(fetchClient), [fetchClient]);
}
