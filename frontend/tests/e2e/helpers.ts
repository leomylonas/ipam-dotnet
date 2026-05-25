// Shared helpers and constants for IPAM E2E tests.
// Both the backend and Vite dev server are managed by Playwright's webServer
// config — no manual startup is required.

import type { APIRequestContext, Page } from '@playwright/test';
import { expect } from '@playwright/test';

// ---------------------------------------------------------------------------
// Credentials
// ---------------------------------------------------------------------------

export const ADMIN_USER = 'admin';
export const ADMIN_PASS = 'Admin1234';

// Reusable passwords for seeded test users — shared across spec files so a
// single change here propagates everywhere.
export const TENANT_ADMIN_PASS = 'Tadmin1234!';
export const TENANT_USER_PASS = 'Tuser1234!';

// Pre-built Authorization header value for the seeded GlobalAdmin account.
export const adminBasicAuth = `Basic ${btoa(`${ADMIN_USER}:${ADMIN_PASS}`)}`;

// ---------------------------------------------------------------------------
// Browser helpers
// ---------------------------------------------------------------------------

/** Fills the login form and waits for the router to land on /. */
export async function loginAs(page: Page, username: string, password: string) {
	await page.goto('/login');
	await page.getByLabel('Username').fill(username);
	await page.getByRole('textbox', { name: 'Password' }).fill(password);
	await page.getByRole('button', { name: 'Sign in' }).click();
	await expect(page).toHaveURL('/');
}

/** Returns a unique name suitable for parallel test runs. */
export function uniqueName(prefix: string): string {
	return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 10000)}`;
}

// ---------------------------------------------------------------------------
// API helpers — all use HTTP Basic Auth so no cookie/session is required.
// ---------------------------------------------------------------------------

/** Creates a tenancy (with an initial TenantAdmin) and returns the parsed response. */
export async function createTenancy(
	request: APIRequestContext,
	name: string,
	adminUsername: string,
	adminPassword: string,
) {
	const response = await request.post('/api/tenancies', {
		headers: { Authorization: adminBasicAuth },
		data: { name, description: `E2E test tenancy: ${name}`, adminUsername, adminPassword },
	});
	if (!response.ok()) {
		throw new Error(`createTenancy failed: ${response.status()} ${await response.text()}`);
	}
	return response.json() as Promise<{ id: string; name: string; description: string; createdAt: string }>;
}

/** Deletes a tenancy by ID (cascades all associated data). */
export async function deleteTenancy(request: APIRequestContext, id: string) {
	const response = await request.delete(`/api/tenancies/${id}`, {
		headers: { Authorization: adminBasicAuth },
	});
	// 404 is acceptable — the tenancy may have already been deleted by a test.
	if (!response.ok() && response.status() !== 404) {
		throw new Error(`deleteTenancy failed: ${response.status()} ${await response.text()}`);
	}
}

/** Creates a shared subnet (GlobalAdmin only) and returns the parsed response. */
export async function createSharedSubnet(request: APIRequestContext, cidr: string, name: string) {
	const response = await request.post('/api/subnets/shared', {
		headers: { Authorization: adminBasicAuth },
		data: { cidr, name, description: `E2E shared subnet: ${name}` },
	});
	if (!response.ok()) {
		throw new Error(`createSharedSubnet failed: ${response.status()} ${await response.text()}`);
	}
	return response.json() as Promise<{
		id: string;
		cidr: string;
		name: string;
		description: string;
		createdAt: string;
	}>;
}

/** Deletes a shared subnet by ID. */
export async function deleteSharedSubnet(request: APIRequestContext, id: string) {
	const response = await request.delete(`/api/subnets/shared/${id}`, {
		headers: { Authorization: adminBasicAuth },
	});
	if (!response.ok() && response.status() !== 404) {
		throw new Error(`deleteSharedSubnet failed: ${response.status()} ${await response.text()}`);
	}
}

/** Creates a private subnet inside a tenancy and returns the parsed response. */
export async function createPrivateSubnet(request: APIRequestContext, tenancyId: string, cidr: string, name: string) {
	const response = await request.post(`/api/tenancies/${tenancyId}/subnets`, {
		headers: { Authorization: adminBasicAuth },
		data: { cidr, name, description: `E2E private subnet: ${name}` },
	});
	if (!response.ok()) {
		throw new Error(`createPrivateSubnet failed: ${response.status()} ${await response.text()}`);
	}
	return response.json() as Promise<{
		id: string;
		cidr: string;
		name: string;
		description: string;
		createdAt: string;
	}>;
}

/** Creates a user account and returns the parsed response. */
export async function createUser(
	request: APIRequestContext,
	username: string,
	password: string,
	role: string,
	tenancyId?: string,
) {
	const response = await request.post('/api/users', {
		headers: { Authorization: adminBasicAuth },
		data: { username, password, role, tenancyId: tenancyId ?? null },
	});
	if (!response.ok()) {
		throw new Error(`createUser failed: ${response.status()} ${await response.text()}`);
	}
	return response.json() as Promise<{ id: string; username: string; role: string; tenancyId: string | null }>;
}
