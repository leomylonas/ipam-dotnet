// Tenancies E2E tests — CRUD lifecycle, modal fields, and role-based access.

import { test, expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import {
	ADMIN_USER,
	ADMIN_PASS,
	TENANT_ADMIN_PASS,
	adminBasicAuth,
	loginAs,
	uniqueName,
	createTenancy,
	deleteTenancy,
} from './helpers';

let redirectTenancyId = '';
let tenantAdminUser = '';
let tenantAdminPass = '';

test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
	tenantAdminUser = uniqueName('ten-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	const tenancy = await createTenancy(request, uniqueName('ten-redirect'), tenantAdminUser, tenantAdminPass);
	redirectTenancyId = tenancy.id;
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	await deleteTenancy(request, redirectTenancyId);
});

// ---------------------------------------------------------------------------
// Navigation and static UI
// ---------------------------------------------------------------------------

test.describe('Tenancies page — navigation and static UI', () => {
	test('navigates to /tenancies and shows the heading', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.getByRole('link', { name: 'Tenancies' }).click();
		await expect(page).toHaveURL('/tenancies');
		await expect(page.getByRole('heading', { name: 'Tenancies' })).toBeVisible();
	});

	test('shows a Create Tenancy button', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/tenancies');
		await expect(page.getByRole('button', { name: 'Create Tenancy' })).toBeVisible();
	});

	test('opens the Create Tenancy modal with all four fields', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/tenancies');
		await page.getByRole('button', { name: 'Create Tenancy' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByLabel('Tenancy name')).toBeVisible();
		await expect(dialog.getByLabel('Description')).toBeVisible();
		await expect(dialog.getByLabel('Admin username')).toBeVisible();
		await expect(dialog.getByLabel('Admin password')).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// Full CRUD lifecycle
// ---------------------------------------------------------------------------

test.describe('Tenancies page — full lifecycle', () => {
	let createdName = '';
	let createdTenancyId = '';

	test.afterAll(async ({ request }: { request: APIRequestContext }) => {
		if (createdTenancyId) {
			await deleteTenancy(request, createdTenancyId);
		}
	});

	test('create → verify in list → edit description → delete → verify removed', async ({ page, request }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/tenancies');

		// --- Create ---
		createdName = uniqueName('ten-lifecycle');
		const adminU = uniqueName('lc-admin');

		await page.getByRole('button', { name: 'Create Tenancy' }).click();
		const dialog = page.getByRole('dialog');
		await dialog.getByLabel('Tenancy name').fill(createdName);
		await dialog.getByLabel('Description').fill('Initial description');
		await dialog.getByLabel('Admin username').fill(adminU);
		await dialog.getByLabel('Admin password').fill('Lcadmin1234!');
		await dialog.getByRole('button', { name: 'Create' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();
		await expect(page.getByRole('cell', { name: createdName })).toBeVisible();

		// Capture the ID via the API for afterAll cleanup.
		const listResp = await request.get('/api/tenancies', { headers: { Authorization: adminBasicAuth } });
		const list = (await listResp.json()) as { id: string; name: string }[];
		const found = list.find((t) => t.name === createdName);
		if (found) {
			createdTenancyId = found.id;
		}

		// --- Edit ---
		const row = page.getByRole('row', { name: new RegExp(createdName, 'i') });
		await row.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Edit' }).click();

		const editDialog = page.getByRole('dialog');
		await expect(editDialog).toBeVisible();
		const descField = editDialog.getByLabel('Description');
		await descField.clear();
		await descField.fill('Updated description');
		await editDialog.getByRole('button', { name: 'Save' }).click();

		await expect(page.getByRole('dialog')).not.toBeVisible();

		// --- Delete ---
		await page
			.getByRole('row', { name: new RegExp(createdName, 'i') })
			.getByRole('button', { name: 'Open menu' })
			.click();
		await page.getByRole('menuitem', { name: 'Delete' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Delete' }).click();

		await expect(page.getByRole('cell', { name: createdName })).not.toBeVisible();
		createdTenancyId = '';
	});
});

// ---------------------------------------------------------------------------
// Role-based access
// ---------------------------------------------------------------------------

test.describe('Tenancies page — role access', () => {
	test('TenantAdmin is redirected from /tenancies to /', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/tenancies');
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});
});
