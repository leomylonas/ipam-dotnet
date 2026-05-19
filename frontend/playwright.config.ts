import { defineConfig, devices } from '@playwright/test';

// E2E tests require both the Vite dev server (started automatically below)
// and the ASP.NET Core backend (start manually with `dotnet run`).
export default defineConfig({
	testDir: './tests/e2e',
	fullyParallel: true,
	// Fail the build on CI if a test is accidentally left with `.only`.
	forbidOnly: !!process.env.CI,
	retries: process.env.CI ? 2 : 0,
	reporter: 'html',

	use: {
		baseURL: 'http://localhost:5173',
		trace: 'on-first-retry',
	},

	projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

	webServer: {
		command: 'pnpm dev',
		url: 'http://localhost:5173',
		// Reuse an already-running dev server in local development to avoid
		// the startup cost on every test run.
		reuseExistingServer: !process.env.CI,
	},
});
