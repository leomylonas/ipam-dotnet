// Allocations E2E tests — list, modal fields, allocate/release lifecycle, bulk,
// tag management, and tag search.

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
	createPrivateSubnet,
} from './helpers';

let tenancyId = '';
let tenantAdminUser = '';
let tenantAdminPass = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	tenantAdminUser = uniqueName('alloc-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;

	const tenancy = await createTenancy(request, uniqueName('alloc-tenancy'), tenantAdminUser, tenantAdminPass);
	tenancyId = tenancy.id;

	await createPrivateSubnet(request, tenancyId, '10.30.0.0/24', uniqueName('alloc-subnet'));
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	await deleteTenancy(request, tenancyId);
});

// ---------------------------------------------------------------------------
// Navigation and static UI
// ---------------------------------------------------------------------------

test.describe('Allocations page — navigation and static UI', () => {
	test('navigates to /allocations and shows the heading', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.getByRole('link', { name: 'Allocations' }).click();
		await expect(page).toHaveURL('/allocations');
		await expect(page.getByRole('heading', { name: 'Allocations' })).toBeVisible();
	});

	test('shows the Allocate IP and Bulk Allocate buttons', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');
		await expect(page.getByRole('button', { name: 'Allocate IP' })).toBeVisible();
		await expect(page.getByRole('button', { name: 'Bulk Allocate' })).toBeVisible();
	});

	test('search bar is visible', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');
		await expect(page.getByPlaceholder('Search…')).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Modal fields
// ---------------------------------------------------------------------------

test.describe('Allocations page — modal fields', () => {
	test('TenantAdmin opens Allocate IP modal — sees Subnet select and Description (no Tenant select)', async ({
		page,
	}) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');
		await page.getByRole('button', { name: 'Allocate IP' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByText('Subnet', { exact: true })).toBeVisible();
		await expect(dialog.getByLabel('Description')).toBeVisible();
		// TenantAdmin callers do not see the Tenant selector.
		await expect(dialog.getByText('Tenant', { exact: true })).not.toBeVisible();
		await dialog.getByRole('button', { name: 'Cancel' }).click();
	});

	test('GlobalAdmin opens Allocate IP modal — sees Tenant select first', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/allocations');
		await page.getByRole('button', { name: 'Allocate IP' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByText('Tenant', { exact: true })).toBeVisible();
		await dialog.getByRole('button', { name: 'Cancel' }).click();
	});
});

// ---------------------------------------------------------------------------
// Allocate → verify → release lifecycle
// ---------------------------------------------------------------------------

test.describe('Allocations — single allocate/release lifecycle', () => {
	test('TenantAdmin allocates an IP, verifies it appears, then releases it', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');

		const desc = `E2E-alloc-${Date.now()}`;

		await page.getByRole('button', { name: 'Allocate IP' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await dialog.locator('select').first().selectOption({ index: 1 });
		await dialog.getByLabel('Description').fill(desc);
		await dialog.getByRole('button', { name: 'Allocate' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		const allocRow = page.getByRole('row', { name: new RegExp(desc, 'i') });
		await expect(allocRow).toBeVisible();

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
// Bulk allocate
// ---------------------------------------------------------------------------

test.describe('Allocations — bulk allocate', () => {
	test('TenantAdmin bulk allocates 2 IPs — both appear with Bulk tag — releases one', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');

		const desc = `E2E-bulk-${Date.now()}`;

		await page.getByRole('button', { name: 'Bulk Allocate' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await dialog.locator('select').first().selectOption({ index: 1 });
		await dialog.getByLabel('Count').fill('2');
		await dialog.getByLabel('Description').fill(desc);
		await dialog.getByRole('button', { name: 'Allocate' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		const bulkRows = page.getByRole('row', { name: new RegExp(desc, 'i') });
		await expect(bulkRows).toHaveCount(2);
		// Use the Carbon tag class — getByText would double-count the nested label span.
		await expect(bulkRows.locator('.cds--tag')).toHaveCount(2);

		// Release the first row.
		await bulkRows.first().getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Release' }).click();
		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Release' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		await expect(page.getByRole('row', { name: new RegExp(desc, 'i') })).toHaveCount(1);

		// Release the remaining row.
		await page
			.getByRole('row', { name: new RegExp(desc, 'i') })
			.first()
			.getByRole('button', { name: 'Open menu' })
			.click();
		await page.getByRole('menuitem', { name: 'Release' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Release' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Tag management
// ---------------------------------------------------------------------------

test.describe('Allocations — tag management', () => {
	test('allocates an IP, adds a tag via Tags modal, verifies tag persists', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/allocations');

		const desc = `E2E-tag-${Date.now()}`;

		await page.getByRole('button', { name: 'Allocate IP' }).click();
		let dialog = page.getByRole('dialog');
		await dialog.locator('select').first().selectOption({ index: 1 });
		await dialog.getByLabel('Description').fill(desc);
		await dialog.getByRole('button', { name: 'Allocate' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		const allocRow = page.getByRole('row', { name: new RegExp(desc, 'i') });
		await expect(allocRow).toBeVisible();

		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Tags' }).click();
		dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();

		await dialog.getByLabel('Key').fill('env');
		await dialog.getByLabel('Value').fill('e2e');
		await dialog.getByRole('button', { name: 'Add' }).click();
		await expect(dialog.getByText('env: e2e')).toBeVisible();
		await dialog.getByRole('button', { name: 'Save Tags' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		// Reopen the modal and verify the tag persisted.
		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Tags' }).click();
		dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByText('env: e2e')).toBeVisible();
		await dialog.getByRole('button', { name: 'Cancel' }).click();

		// Clean up.
		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Release' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Release' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Tag search
// ---------------------------------------------------------------------------

test.describe('Allocations — search by tag', () => {
	test('tag filter shows only matching allocation', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/allocations');

		const desc = `E2E-search-${Date.now()}`;
		const tagKey = `srchkey${Date.now()}`;
		const tagVal = `srchval${Date.now()}`;

		// Allocate as GlobalAdmin — select a tenancy then the private subnet.
		await page.getByRole('button', { name: 'Allocate IP' }).click();
		let dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();

		const tenantSelect = dialog.locator('select').first();
		await tenantSelect.selectOption({ index: 1 });
		// Wait for subnet options to populate after tenancy selection.
		const subnetSelect = dialog.locator('select').nth(1);
		await expect(subnetSelect.locator('option').nth(1)).not.toHaveText('Select a subnet…', { timeout: 5000 });
		await subnetSelect.selectOption({ index: 1 });

		await dialog.getByLabel('Description').fill(desc);
		await dialog.getByRole('button', { name: 'Allocate' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		const allocRow = page.getByRole('row', { name: new RegExp(desc, 'i') });
		await expect(allocRow).toBeVisible();

		// Tag the allocation.
		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Tags' }).click();
		dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await dialog.getByLabel('Key').fill(tagKey);
		await dialog.getByLabel('Value').fill(tagVal);
		await dialog.getByRole('button', { name: 'Add' }).click();
		// Wait for the tag entry to render before saving so the mutation sends the correct payload.
		// Carbon Tag duplicates text in a tooltip span — use first() to avoid strict mode.
		await expect(dialog.getByText(tagKey).first()).toBeVisible();
		await dialog.getByRole('button', { name: 'Save Tags' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		// Search using key=value syntax.
		await page.getByPlaceholder('Search…').fill(`${tagKey}=${tagVal}`);
		// After the debounce the tagged allocation should be visible.
		await expect(page.getByRole('row', { name: new RegExp(desc, 'i') })).toBeVisible({ timeout: 3000 });

		// Clear search before cleanup so the release row can be found.
		await page.getByPlaceholder('Search…').fill('');

		// Clean up.
		await allocRow.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Release' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Release' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();
	});
});
