import { test, expect } from '@playwright/test';
import { ADMIN_USER, ADMIN_PASS } from './helpers';

test.describe('Login', () => {
	test('redirects unauthenticated users to the login page', async ({ page }) => {
		await page.goto('/');
		await expect(page).toHaveURL('/login');
		await expect(page.getByRole('heading', { name: 'IPAM' })).toBeVisible();
	});

	test('shows an error for invalid credentials', async ({ page }) => {
		await page.goto('/login');
		await page.getByLabel('Username').fill(ADMIN_USER);
		await page.getByRole('textbox', { name: 'Password' }).fill('wrong-password');
		await page.getByRole('button', { name: 'Sign in' }).click();
		await expect(page.getByText(/invalid username or password/i)).toBeVisible();
	});

	test('logs in and reaches the dashboard', async ({ page }) => {
		await page.goto('/login');
		await page.getByLabel('Username').fill(ADMIN_USER);
		await page.getByRole('textbox', { name: 'Password' }).fill(ADMIN_PASS);
		await page.getByRole('button', { name: 'Sign in' }).click();
		await expect(page).toHaveURL('/');
		await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
	});

	test('logs out and returns to the login page', async ({ page }) => {
		await page.goto('/login');
		await page.getByLabel('Username').fill(ADMIN_USER);
		await page.getByRole('textbox', { name: 'Password' }).fill(ADMIN_PASS);
		await page.getByRole('button', { name: 'Sign in' }).click();
		await expect(page).toHaveURL('/');

		await page.getByRole('button', { name: /log out/i }).click();
		await expect(page).toHaveURL('/login');
	});
});
