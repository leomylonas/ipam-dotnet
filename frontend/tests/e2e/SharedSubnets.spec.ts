// Shared Subnets E2E tests — list, detail page, access management, exclusions, and role access.

import { test, expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import {
	ADMIN_USER,
	ADMIN_PASS,
	TENANT_ADMIN_PASS,
	loginAs,
	uniqueName,
	createTenancy,
	deleteTenancy,
	createSharedSubnet,
	deleteSharedSubnet,
} from './helpers';

let seedSubnetId = '';
let seedSubnetName = '';
let accessTenancyId = '';
let tenantAdminUser = '';
let tenantAdminPass = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	seedSubnetName = uniqueName('shared-seed');
	const subnet = await createSharedSubnet(request, '203.0.113.0/28', seedSubnetName);
	seedSubnetId = subnet.id;

	tenantAdminUser = uniqueName('ss-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	const tenancy = await createTenancy(request, uniqueName('ss-tenancy'), tenantAdminUser, tenantAdminPass);
	accessTenancyId = tenancy.id;
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	if (seedSubnetId) {
		// The UI delete test may have already removed this subnet.
		try {
			await deleteSharedSubnet(request, seedSubnetId);
		} catch {
			// ignored
		}
	}
	await deleteTenancy(request, accessTenancyId);
});

// ---------------------------------------------------------------------------
// Navigation and static UI
// ---------------------------------------------------------------------------

test.describe('Shared Subnets page — navigation and static UI', () => {
	test('navigates to /shared-subnets and shows the heading', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');
		await expect(page.getByRole('heading', { name: 'Shared Subnets' })).toBeVisible();
	});

	test('shows the Create Shared Subnet button', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');
		await expect(page.getByRole('button', { name: 'Create Shared Subnet' })).toBeVisible();
	});

	test('opens the Create Shared Subnet modal with CIDR, Name, Description fields', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');
		await page.getByRole('button', { name: 'Create Shared Subnet' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByLabel('CIDR')).toBeVisible();
		await expect(dialog.getByLabel('Name')).toBeVisible();
		await expect(dialog.getByLabel('Description')).toBeVisible();
	});

	test('seed subnet name is visible in the list', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');
		// Use first() — the description cell also contains the name as a substring.
		await expect(page.getByRole('cell', { name: seedSubnetName }).first()).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Detail page
// ---------------------------------------------------------------------------

test.describe('Shared Subnets — detail page', () => {
	test('clicking the subnet row navigates to the detail page', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');
		await page.getByRole('row', { name: new RegExp(seedSubnetName, 'i') }).click();
		await expect(page).toHaveURL(new RegExp(`/shared-subnets/${seedSubnetId}`));
	});

	test('detail page shows SubnetMetrics labels', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Total IPs')).toBeVisible();
		await expect(page.getByText('Allocated').first()).toBeVisible();
		await expect(page.getByText('Free')).toBeVisible();
		await expect(page.getByText('Excluded')).toBeVisible();
	});

	test('detail page shows Tenancy Access section', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Tenancy Access')).toBeVisible();
	});

	test('detail page shows Exclusion Ranges section', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Exclusion Ranges')).toBeVisible();
	});

	test('detail page shows Allocations section', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByRole('heading', { name: 'Allocations' })).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Tenancy access management
// ---------------------------------------------------------------------------

test.describe('Shared Subnets — tenancy access management', () => {
	test('grant access to a tenancy → tenancy appears in the access list', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Tenancy Access')).toBeVisible();
		await page.getByRole('button', { name: 'Grant Access' }).click();

		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await dialog.locator('select').selectOption({ index: 1 });
		await dialog.getByRole('button', { name: 'Grant' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		// The tenancy name (uniqueName-based, contains 'ss-tenancy') should appear
		// in the access table Name column.
		await expect(page.getByRole('cell', { name: /ss-tenancy/i }).first()).toBeVisible();
	});

	test('revoke access → tenancy is removed from the access list', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Tenancy Access')).toBeVisible();

		const accessRow = page.getByRole('row', { name: /ss-tenancy/i });
		// Wait for the access table to load; skip if the grant test didn't run.
		const appeared = await accessRow
			.waitFor({ state: 'visible', timeout: 5000 })
			.then(() => true)
			.catch(() => false);
		if (!appeared) {
			test.skip();
			return;
		}

		await accessRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Revoke' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Revoke' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		await expect(page.getByRole('row', { name: /ss-tenancy/i })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Exclusions
// ---------------------------------------------------------------------------

test.describe('Shared Subnets — exclusions on detail page', () => {
	test('adds an exclusion range and verifies it appears', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto(`/shared-subnets/${seedSubnetId}`);
		await expect(page.getByText('Exclusion Ranges')).toBeVisible();

		await page.getByRole('button', { name: 'Add Exclusion' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();

		// Use IPs within the 203.0.113.0/28 range (hosts: .1 – .14).
		await dialog.getByLabel('Start IP').fill('203.0.113.5');
		await dialog.getByLabel('End IP').fill('203.0.113.5');
		await dialog.getByLabel('Description').fill('E2E exclusion test');
		await dialog.getByRole('button', { name: 'Add' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();
		await expect(page.getByText('203.0.113.5')).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Full UI CRUD lifecycle
// ---------------------------------------------------------------------------

test.describe('Shared Subnets — create and delete via UI', () => {
	test('creates a shared subnet via the form and deletes it via overflow menu', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/shared-subnets');

		const uiCreatedSubnetName = uniqueName('ss-ui');

		await page.getByRole('button', { name: 'Create Shared Subnet' }).click();
		const dialog = page.getByRole('dialog');
		await dialog.getByLabel('CIDR').fill('203.0.113.32/28');
		await dialog.getByLabel('Name').fill(uiCreatedSubnetName);
		await dialog.getByLabel('Description').fill('Created by UI E2E test');
		await dialog.getByRole('button', { name: 'Create' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();
		await expect(page.getByRole('cell', { name: uiCreatedSubnetName })).toBeVisible();

		const row = page.getByRole('row', { name: new RegExp(uiCreatedSubnetName, 'i') });
		await row.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Delete' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Delete' }).click();

		await expect(page.getByRole('cell', { name: uiCreatedSubnetName })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Role-based access
// ---------------------------------------------------------------------------

test.describe('Shared Subnets page — role access', () => {
	test('TenantAdmin navigating to /shared-subnets is redirected to /', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/shared-subnets');
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});
});
