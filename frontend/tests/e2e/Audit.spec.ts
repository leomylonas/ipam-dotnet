// Audit E2E tests — heading, table columns, row presence, and role-based access.

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
	createPrivateSubnet,
	createUser,
} from './helpers';

let tenancyId = '';
let tenantAdminUser = '';
let tenantAdminPass = '';
let tenantUserUser = '';
let tenantUserPass = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	tenantAdminUser = uniqueName('aud-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	tenantUserUser = uniqueName('aud-tuser');
	tenantUserPass = TENANT_USER_PASS;

	const tenancy = await createTenancy(request, uniqueName('aud-tenancy'), tenantAdminUser, tenantAdminPass);
	tenancyId = tenancy.id;

	await createUser(request, tenantUserUser, tenantUserPass, 'TenantUser', tenancyId);

	// Allocate an IP using TenantAdmin credentials so the audit record carries the
	// tenancy ID and is visible in the TenantAdmin scoped audit view.
	const subnet = await createPrivateSubnet(request, tenancyId, '10.40.0.0/24', uniqueName('aud-subnet'));
	const tenantAdminAuth = `Basic ${btoa(`${tenantAdminUser}:${tenantAdminPass}`)}`;
	await request.post('/api/allocations', {
		headers: { Authorization: tenantAdminAuth },
		data: { subnetId: subnet.id, description: 'Audit E2E setup allocation' },
	});
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	await deleteTenancy(request, tenancyId);
});

// ---------------------------------------------------------------------------
// GlobalAdmin audit access
// ---------------------------------------------------------------------------

test.describe('Audit page — GlobalAdmin', () => {
	test('navigates to /audit and sees the "Audit Log" heading', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/audit');
		await expect(page).toHaveURL('/audit');
		await expect(page.getByRole('heading', { name: 'Audit Log' })).toBeVisible();
	});

	test('audit table has Timestamp, Action, and User column headers', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/audit');
		await expect(page.getByRole('columnheader', { name: 'Timestamp' })).toBeVisible();
		await expect(page.getByRole('columnheader', { name: 'Action' })).toBeVisible();
		await expect(page.getByRole('columnheader', { name: 'User' })).toBeVisible();
	});

	test('at least one audit row is visible after API-seeded allocation', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/audit');
		await expect(page.getByRole('cell', { name: 'Allocated' }).first()).toBeVisible({ timeout: 8000 });
	});
});

// ---------------------------------------------------------------------------
// TenantAdmin audit access
// ---------------------------------------------------------------------------

test.describe('Audit page — TenantAdmin', () => {
	test('navigates to /audit and sees the "Audit Log" heading', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/audit');
		await expect(page).toHaveURL('/audit');
		await expect(page.getByRole('heading', { name: 'Audit Log' })).toBeVisible();
	});

	test('shows at least one row scoped to the tenancy', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/audit');
		// The setup allocation was made with TenantAdmin credentials so it appears here.
		await expect(page.getByRole('cell', { name: 'Allocated' }).first()).toBeVisible({ timeout: 8000 });
	});
});

// ---------------------------------------------------------------------------
// TenantUser — redirected
// ---------------------------------------------------------------------------

test.describe('Audit page — TenantUser', () => {
	test('navigating to /audit redirects TenantUser to /', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await page.goto('/audit');
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});
});
