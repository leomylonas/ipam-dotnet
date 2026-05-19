import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { useFetchClient } from '../Utils/FetchClient';
import { z } from 'zod';

/** Schema and constants for the three permitted roles — mirrors Roles.cs on the backend. */
export const roleSchema = z.enum(['GlobalAdmin', 'TenantAdmin', 'TenantUser']);

/** Union type of the three role strings. */
export type Role = z.infer<typeof roleSchema>;

/** Role name constants derived from the schema — use these instead of raw string literals. */
export const Roles = roleSchema.enum;

/** Human-friendly labels for each role, used in the UI. */
export const roleLabel: Record<Role, string> = {
	[Roles.GlobalAdmin]: 'Global Admin',
	[Roles.TenantAdmin]: 'Tenant Admin',
	[Roles.TenantUser]: 'Tenant User',
};

/**
 * Zod schema for the AuthMeResponse DTO returned by POST /auth/login and
 * GET /auth/me. Validates that the server response matches the shape the
 * frontend expects before storing it in auth state.
 */
export const authResponseSchema = z.object({
	id: z.string(),
	username: z.string(),
	// Validated against the three known roles — an unrecognised value from the
	// server will throw a parse error rather than silently flowing as a string.
	role: roleSchema,
	// TenancyId is null for GlobalAdmin accounts.
	tenancyId: z.uuid().nullable(),
});
export type AuthResponse = z.infer<typeof authResponseSchema>;

/** Schema for the login request body — used for client-side form validation. */
export const loginRequestSchema = z.object({
	username: z.string().min(1, 'Username is required'),
	password: z.string().min(1, 'Password is required'),
});
export type LoginRequest = z.infer<typeof loginRequestSchema>;

/**
 * Auth API class. Each method is a thin wrapper around FetchClient that
 * validates the server response with the corresponding Zod schema.
 *
 * Non-2xx responses throw a FetchClientError carrying the Problem Details body
 * from the backend — callers catch and surface the error as appropriate.
 *
 * Instantiate directly for one-off use (e.g. the startup auth check in
 * Main.tsx), or use the useAuth() hook to get a memoised instance tied to
 * the singleton FetchClient.
 */
export class AuthService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * POST /auth/login — submits credentials and receives an auth cookie on
	 * success. The validated user profile is returned so the caller can update
	 * auth state immediately without a separate /auth/me round-trip.
	 */
	login(request: LoginRequest): Promise<AuthResponse> {
		return this.client.postJson('/auth/login', request, authResponseSchema);
	}

	/**
	 * POST /auth/logout — clears the auth cookie server-side. Returns a 204 on
	 * success; the caller does not need the response body.
	 */
	logout(): Promise<void> {
		return this.client.postJson('/auth/logout', {});
	}

	/**
	 * GET /auth/me — returns the current user's profile when a valid cookie
	 * exists. Used on app load to restore auth state from an existing session.
	 * Throws FetchClientError (401) when unauthenticated.
	 */
	me(): Promise<AuthResponse> {
		return this.client.getJson('/auth/me', authResponseSchema);
	}
}

/**
 * Hook that returns a memoised Auth instance backed by the singleton
 * FetchClient. The instance is stable as long as the client reference does not
 * change, so consumers can safely use it as a stable dependency in effects.
 */
export function useAuthService(): AuthService {
	const fetchClient = useFetchClient();
	return useMemo(() => new AuthService(fetchClient), [fetchClient]);
}
