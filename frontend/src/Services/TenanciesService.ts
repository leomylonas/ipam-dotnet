import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';
import { passwordSchema } from './UsersService';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for a tenancy as returned by the API. All fields match the
 * TenancyResponse DTO on the backend.
 */
export const tenancySchema = z.object({
	id: z.uuid(),
	name: z.string(),
	description: z.string(),
	/** ISO-8601 UTC datetime string — format with `new Date()` in the UI. */
	createdAt: z.string(),
});
export type Tenancy = z.infer<typeof tenancySchema>;

/**
 * Validation schema for the POST /api/tenancies request body. Creating a
 * tenancy also creates the first TenantAdmin user, so admin credentials
 * must be supplied upfront.
 */
export const createTenancySchema = z.object({
	name: z.string().min(1, 'Name is required'),
	description: z.string().min(1, 'Description is required'),
	adminUsername: z.string().min(1, 'Admin username is required'),
	adminPassword: passwordSchema,
});
export type CreateTenancyRequest = z.infer<typeof createTenancySchema>;

/**
 * Validation schema for PUT /api/tenancies/{id}. Only name and description
 * are editable after creation.
 */
export const updateTenancySchema = z.object({
	name: z.string().min(1, 'Name is required'),
	description: z.string().min(1, 'Description is required'),
});
export type UpdateTenancyRequest = z.infer<typeof updateTenancySchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of tenancies. */
const tenancyListSchema = z.array(tenancySchema);

/**
 * Service class for all tenancy-related API operations.
 * All methods are thin wrappers around FetchClient that validate the server
 * response with the corresponding Zod schema before returning.
 */
export class TenanciesService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/tenancies — returns all tenancies. GlobalAdmin only.
	 */
	list(): Promise<Tenancy[]> {
		return this.client.getJson('/api/tenancies', tenancyListSchema);
	}

	/**
	 * POST /api/tenancies — creates a tenancy and its initial TenantAdmin user
	 * in a single atomic operation.
	 */
	create(request: CreateTenancyRequest): Promise<Tenancy> {
		return this.client.postJson('/api/tenancies', request, tenancySchema);
	}

	/**
	 * PUT /api/tenancies/{id} — updates a tenancy's name and description.
	 */
	update(id: string, request: UpdateTenancyRequest): Promise<Tenancy> {
		return this.client.putJson(`/api/tenancies/${id}`, request, tenancySchema);
	}

	/**
	 * DELETE /api/tenancies/{id} — deletes a tenancy and all associated data.
	 * Returns void on success (HTTP 204).
	 */
	delete(id: string): Promise<void> {
		return this.client.deleteJson(`/api/tenancies/${id}`);
	}
}

/**
 * Hook that returns a memoised TenanciesService backed by the singleton
 * FetchClient. The instance is stable for the lifetime of the component.
 */
export function useTenanciesService(): TenanciesService {
	const fetchClient = useFetchClient();
	return useMemo(() => new TenanciesService(fetchClient), [fetchClient]);
}
