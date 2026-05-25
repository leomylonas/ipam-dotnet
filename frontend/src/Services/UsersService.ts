import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';
import { roleSchema } from './AuthService';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Shared password schema for any field where a new password is being set.
 * Mirrors ASP.NET Identity's default password requirements so validation
 * failures are caught client-side before the request is sent.
 *
 * Do NOT use this for login — the login field only needs to be non-empty.
 */
export const passwordSchema = z
	.string()
	.min(8, 'Password must be at least 8 characters.')
	.regex(/[0-9]/, "Password must have at least one digit ('0'-'9').")
	.regex(/[A-Z]/, "Password must have at least one uppercase ('A'-'Z').");

/**
 * Zod schema for a user as returned by the API. Mirrors the UserResponse DTO.
 * Passwords are never included in responses.
 */
export const userSchema = z.object({
	/** ASP.NET Identity user ID (string GUID). */
	id: z.string(),
	username: z.string(),
	/** The user's role — one of the three known role strings. */
	role: roleSchema,
	/** Null for GlobalAdmin accounts. */
	tenancyId: z.uuid().nullable(),
});
export type User = z.infer<typeof userSchema>;

/**
 * Validation schema for POST /api/users. GlobalAdmin may set any role and
 * tenancy; TenantAdmin is restricted to TenantUser in their own tenancy.
 */
export const createUserSchema = z.object({
	username: z.string().min(1, 'Username is required'),
	password: passwordSchema,
	role: roleSchema,
	tenancyId: z.uuid().nullable(),
});
export type CreateUserRequest = z.infer<typeof createUserSchema>;

/**
 * Validation schema for PUT /api/users/{id}. All fields except password are
 * required. TenantUser callers may only supply password for their own account.
 */
export const updateUserSchema = z.object({
	username: z.string().min(1, 'Username is required'),
	role: roleSchema,
	tenancyId: z.uuid().nullable(),
	/** Optional new password — when present the password is changed atomically. */
	password: passwordSchema.optional(),
});
export type UpdateUserRequest = z.infer<typeof updateUserSchema>;

/**
 * Minimal schema used when the caller is a TenantUser updating their own
 * password only. The backend enforces that no other fields are changed.
 */
export const changePasswordSchema = z.object({
	password: passwordSchema,
});
export type ChangePasswordRequest = z.infer<typeof changePasswordSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of users. */
const userListSchema = z.array(userSchema);

/**
 * Service class for all user-related API operations.
 * GlobalAdmin sees all users; TenantAdmin is scoped to their own tenancy.
 */
export class UsersService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/users — returns users visible to the caller.
	 * GlobalAdmin receives all users; TenantAdmin receives only their tenancy's users.
	 */
	list(): Promise<User[]> {
		return this.client.getJson('/api/users', userListSchema);
	}

	/**
	 * POST /api/users — creates a new user.
	 * GlobalAdmin can assign any role and tenancy. TenantAdmin can only create
	 * TenantUser accounts within their own tenancy.
	 */
	create(request: CreateUserRequest): Promise<User> {
		return this.client.postJson('/api/users', request, userSchema);
	}

	/**
	 * PUT /api/users/{id} — updates a user's username, role, tenancy, and
	 * optionally their password. TenantUser callers may only change their own
	 * password.
	 */
	update(id: string, request: UpdateUserRequest): Promise<User> {
		return this.client.putJson(`/api/users/${id}`, request, userSchema);
	}

	/**
	 * DELETE /api/users/{id} — deletes a user account.
	 * GlobalAdmin can delete any user; TenantAdmin can only delete within their tenancy.
	 */
	delete(id: string): Promise<void> {
		return this.client.deleteJson(`/api/users/${id}`);
	}
}

/**
 * Hook that returns a memoised UsersService backed by the singleton FetchClient.
 */
export function useUsersService(): UsersService {
	const fetchClient = useFetchClient();
	return useMemo(() => new UsersService(fetchClient), [fetchClient]);
}
