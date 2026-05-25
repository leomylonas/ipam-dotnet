// Dashboard E2E tests — covers GlobalAdmin, TenantAdmin, and TenantUser views.

import { test, expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import {
	ADMIN_USER,
	ADMIN_PASS,
	TENANT_ADMIN_PASS,
	TENANT_USER_PASS,
	loginAs,
	uniqueName,
	createTenancy,
	deleteTenancy,
	createUser,
} from './helpers';

let tenancyId = '';
let tenantAdminUser = '';
let tenantAdminPass = '';
let tenantUserUser = '';
let tenantUserPass = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	tenantAdminUser = uniqueName('dash-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	tenantUserUser = uniqueName('dash-tuser');
	tenantUserPass = TENANT_USER_PASS;

	const tenancy = await createTenancy(request, uniqueName('dash-tenancy'), tenantAdminUser, tenantAdminPass);
	tenancyId = tenancy.id;

	await createUser(request, tenantUserUser, tenantUserPass, 'TenantUser', tenancyId);
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	await deleteTenancy(request, tenancyId);
});

// ---------------------------------------------------------------------------
// GlobalAdmin dashboard
// ---------------------------------------------------------------------------

test.describe('Dashboard — GlobalAdmin', () => {
	test('shows the Dashboard heading', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});

	test('shows global system metrics (Tenancies, Users, Shared Subnets)', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		// Use exact match to avoid matching nav links that also contain these words.
		await expect(page.getByText('Tenancies', { exact: true }).first()).toBeVisible();
		await expect(page.getByText('Users', { exact: true }).first()).toBeVisible();
		await expect(page.getByText('Shared Subnets', { exact: true }).first()).toBeVisible();
	});

	test('shows the Recent Activity section', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await expect(page.getByRole('heading', { name: 'Recent Activity' })).toBeVisible();
	});

	test('nav shows Tenancies and Shared Subnets links', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await expect(page.getByRole('link', { name: 'Tenancies' })).toBeVisible();
		await expect(page.getByRole('link', { name: 'Shared Subnets' })).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// TenantAdmin dashboard
// ---------------------------------------------------------------------------

test.describe('Dashboard — TenantAdmin', () => {
	test('shows the Dashboard heading', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});

	test('shows Private Subnets metric label (not Tenancies)', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await expect(page.getByText('Private Subnets')).toBeVisible();
		await expect(page.getByText('Tenancies')).not.toBeVisible();
	});

	test('shows the Recent Activity section', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await expect(page.getByRole('heading', { name: 'Recent Activity' })).toBeVisible();
	});

	test('nav does NOT show Tenancies or Shared Subnets links', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await expect(page.getByRole('link', { name: 'Tenancies' })).not.toBeVisible();
		await expect(page.getByRole('link', { name: 'Shared Subnets' })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// TenantUser dashboard
// ---------------------------------------------------------------------------

test.describe('Dashboard — TenantUser', () => {
	test('shows the Dashboard heading', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});

	test('shows the Accessible Subnets section', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await expect(page.getByRole('heading', { name: 'Accessible Subnets' })).toBeVisible();
	});

	test('shows the Recent Allocations section', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await expect(page.getByRole('heading', { name: 'Recent Allocations' })).toBeVisible();
	});
});
