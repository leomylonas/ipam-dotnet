// Users E2E tests — heading, modal fields, CRUD, and role-based access.

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
	tenantAdminUser = uniqueName('usr-tadmin');
	tenantAdminPass = TENANT_ADMIN_PASS;
	tenantUserUser = uniqueName('usr-tuser');
	tenantUserPass = TENANT_USER_PASS;

	const tenancy = await createTenancy(request, uniqueName('usr-tenancy'), tenantAdminUser, tenantAdminPass);
	tenancyId = tenancy.id;

	await createUser(request, tenantUserUser, tenantUserPass, 'TenantUser', tenancyId);
});

test.afterAll(async ({ request }: { request: APIRequestContext }) => {
	await deleteTenancy(request, tenancyId);
});

// ---------------------------------------------------------------------------
// GlobalAdmin — navigation and static UI
// ---------------------------------------------------------------------------

test.describe('Users page — GlobalAdmin', () => {
	test('navigates to /users and shows the heading', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/users');
		await expect(page).toHaveURL('/users');
		await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
	});

	test('shows a Create User button', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/users');
		await expect(page.getByRole('button', { name: 'Create User' })).toBeVisible();
	});

	test('opens the Create User modal with Username, Password, Role fields', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/users');
		await page.getByRole('button', { name: 'Create User' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog).toBeVisible();
		await expect(dialog.getByLabel('Username')).toBeVisible();
		await expect(dialog.getByLabel('Password')).toBeVisible();
		await expect(dialog.getByText('Role')).toBeVisible();
	});

	test('creates a TenantUser → appears in list → deletes it → removed from list', async ({ page }) => {
		await loginAs(page, ADMIN_USER, ADMIN_PASS);
		await page.goto('/users');

		const newUser = uniqueName('usr-new-tuser');
		await page.getByRole('button', { name: 'Create User' }).click();
		const dialog = page.getByRole('dialog');

		await dialog.getByLabel('Username').fill(newUser);
		await dialog.getByLabel('Password').fill('Newuser1234!');
		// Carbon uses a native <select> — first select is Role.
		await dialog.locator('select').first().selectOption('TenantUser');
		// Second select is Tenancy — pick the first non-placeholder option.
		await expect(dialog.locator('select').nth(1)).toBeVisible();
		await dialog.locator('select').nth(1).selectOption({ index: 1 });

		await dialog.getByRole('button', { name: 'Create' }).click();
		await expect(page.getByRole('dialog')).not.toBeVisible();

		await expect(page.getByRole('cell', { name: newUser })).toBeVisible();

		const row = page.getByRole('row', { name: new RegExp(newUser, 'i') });
		await row.getByRole('button', { name: 'Open menu' }).click();
		await page.getByRole('menuitem', { name: 'Delete' }).click();

		const confirmDialog = page.getByRole('dialog');
		await expect(confirmDialog).toBeVisible();
		await confirmDialog.getByRole('button', { name: 'Delete' }).click();

		await expect(page.getByRole('cell', { name: newUser })).not.toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// TenantAdmin — can access /users
// ---------------------------------------------------------------------------

test.describe('Users page — TenantAdmin', () => {
	test('navigates to /users and sees the heading', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/users');
		await expect(page).toHaveURL('/users');
		await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
	});

	test('Create User button is visible for TenantAdmin', async ({ page }) => {
		await loginAs(page, tenantAdminUser, tenantAdminPass);
		await page.goto('/users');
		await expect(page.getByRole('button', { name: 'Create User' })).toBeVisible();
	});
});

// ---------------------------------------------------------------------------
// TenantUser — redirected from /users
// ---------------------------------------------------------------------------

test.describe('Users page — TenantUser', () => {
	test('navigating to /users redirects TenantUser to /', async ({ page }) => {
		await loginAs(page, tenantUserUser, tenantUserPass);
		await page.goto('/users');
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});
});
