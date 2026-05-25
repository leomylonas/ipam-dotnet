// Private Subnets E2E tests — list, detail page, exclusions, allocations, and role access.

import { test, expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import {
	loginAs,
	uniqueName,
	TENANT_ADMIN_PASS,
	TENANT_USER_PASS,
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
let seedSubnetId = '';
let seedSubnetName = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	tenantAdminUser = uniqueName('sn-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	tenantUserUser = uniqueName('sn-tuser');
	tenantUserPass = TENANT_USER_PASS;

	const tenancy = await createTenancy(request, uniqueName('sn-tenancy'), tenantAdminUser, tenantAdminPass);
	tenancyId = tenancy.id;

	await createUser(request, tenantUserUser, tenantUserPass, 'TenantUser', tenancyId);

	seedSubnetName = uniqueName('sn-seed');
	const subnet = await createPrivateSubnet(request, tenancyId, '10.20.0.0/24', seedSubnetName);
	seedSubnetId = subnet.id;
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	// Cascades subnets, allocations, exclusions.
	await deleteTenancy(request, tenancyId);
});

// ---------------------------------------------------------------------------
// Navigation and static UI (TenantAdmin)
// ---------------------------------------------------------------------------

test.describe('Subnets page — navigation and static UI', () => {
	test('TenantAdmin navigates to /subnets and sees the heading', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');
		await expect(page).toHaveURL('/subnets');
		await expect(page.getByRole('heading', { name: 'Subnets' })).toBeVisible();
	});

	test('TenantAdmin sees the Create Subnet button', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');
		await expect(page.getByRole('button', { name: 'Create Subnet' })).toBeVisible();
	});

	test('opens the Create Subnet modal with CIDR, Name, Description fields', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');
		await page.getByRole('button', { name: 'Create Subnet' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByLabel('CIDR')).toBeVisible();
		await expect(dialog.getByLabel('Name')).toBeVisible();
		await expect(dialog.getByLabel('Description')).toBeVisible();
		await dialog.getByRole('button', { name: 'Cancel' }).click();
	});

	test('seed subnet name appears in the list', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');
		// Use exact:true so the Description cell ("E2E private subnet: <name>") doesn't also match.
		await expect(page.getByRole('cell', { name: seedSubnetName, exact: true })).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Detail page (TenantAdmin)
// ---------------------------------------------------------------------------

test.describe('Subnets — detail page', () => {
	test('clicking the subnet row navigates to the detail page', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');
		await page.getByRole('row', { name: new RegExp(seedSubnetName, 'i') }).click();
		await expect(page).toHaveURL(new RegExp(`/subnets/${seedSubnetId}`));
	});

	test('detail page shows SubnetMetrics labels', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto(`/subnets/${seedSubnetId}`);
		await expect(page.getByText('Total IPs')).toBeVisible();
		// Use first() to avoid strict mode if "Allocated" appears in both the metric
		// label and any allocation table cells.
		await expect(page.getByText('Allocated').first()).toBeVisible();
		await expect(page.getByText('Free')).toBeVisible();
		await expect(page.getByText('Excluded')).toBeVisible();
	});

	test('detail page shows Exclusion Ranges section', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto(`/subnets/${seedSubnetId}`);
		await expect(page.getByText('Exclusion Ranges')).toBeVisible();
	});

	test('detail page shows Allocations section', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto(`/subnets/${seedSubnetId}`);
		// Use heading role to avoid matching the "Allocations" sidebar nav link.
		await expect(page.getByRole('heading', { name: 'Allocations' })).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Exclusions on detail page
// ---------------------------------------------------------------------------

test.describe('Subnets — exclusions on detail page', () => {
	test('adds an exclusion and verifies it appears', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto(`/subnets/${seedSubnetId}`);
		await expect(page.getByText('Exclusion Ranges')).toBeVisible();

		await page.getByRole('button', { name: 'Add Exclusion' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();

		// Use a valid host inside 10.20.0.0/24 — avoid .0 (network) and .255 (broadcast).
		await dialog.getByLabel('Start IP').fill('10.20.0.10');
		await dialog.getByLabel('End IP').fill('10.20.0.10');
		await dialog.getByLabel('Description').fill('E2E exclusion');
		await dialog.getByRole('button', { name: 'Add' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();
		await expect(page.getByText('10.20.0.10')).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Allocations on detail page
// ---------------------------------------------------------------------------

test.describe('Subnets — allocations on detail page', () => {
	test('allocation appears on detail page and can be released from there', async ({ page }) => {
		const desc = `E2E-detail-${Date.now()}`;

		// Allocate via the /allocations page so the AllocationSection has data to show.
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');
		await page.getByRole('button', { name: 'Allocate IP' }).click();
		const dialog = page.getByRole('dialog');
		// Select the seed subnet by ID so cross-file parallel runs don't pick a shared subnet.
		await dialog.locator('select').first().selectOption({ value: seedSubnetId });
		await dialog.getByLabel('Description').fill(desc);
		await dialog.getByRole('button', { name: 'Allocate' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		// Navigate to the subnet detail page and verify the allocation appears.
		await page.goto(`/subnets/${seedSubnetId}`);
		const allocRow = page.getByRole('row', { name: new RegExp(desc, 'i') });
		await expect(allocRow).toBeVisible();

		// Release it via the overflow menu on the detail page.
		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Release' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Release' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		await expect(page.getByRole('row', { name: new RegExp(desc, 'i') })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Full UI CRUD lifecycle (TenantAdmin creates and deletes a subnet via UI)
// ---------------------------------------------------------------------------

test.describe('Subnets — create and delete via UI', () => {
	test('TenantAdmin creates a subnet via form and deletes it via overflow menu', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/subnets');

		const newSubnetName = uniqueName('sn-ui');

		await page.getByRole('button', { name: 'Create Subnet' }).click();
		const dialog = page.getByRole('dialog');
		await dialog.getByLabel('CIDR').fill('10.20.1.0/28');
		await dialog.getByLabel('Name').fill(newSubnetName);
		await dialog.getByLabel('Description').fill('UI-created E2E subnet');
		await dialog.getByRole('button', { name: 'Create' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();
		await expect(page.getByRole('cell', { name: newSubnetName })).toBeVisible();

		const row = page.getByRole('row', { name: new RegExp(newSubnetName, 'i') });
		await row.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Delete' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Delete' }).click();

		await expect(page.getByRole('cell', { name: newSubnetName })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Role-based access
// ---------------------------------------------------------------------------

test.describe('Subnets page — role access', () => {
	test('TenantUser navigating to /subnets is redirected to /', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await page.goto('/subnets');
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});
});
