import { defineConfig, devices } from '@playwright/test';
import * as os from 'os';
import * as path from 'path';

// Unique temp database path per test session so every run starts from a clean state.
const e2eDbPath = path.join(os.tmpdir(), `ipam-e2e-${Date.now()}.db`);

// Dedicated ports for the E2E stack so they never collide with the dev server
// (port 5101 / 5173) that a developer may have running in another terminal.
const E2E_BACKEND_PORT = 5201;
const E2E_FRONTEND_PORT = 5174;
const E2E_BACKEND_URL = `http://localhost:${E2E_BACKEND_PORT}`;
const E2E_FRONTEND_URL = `http://localhost:${E2E_FRONTEND_PORT}`;

export default defineConfig({
	testDir: './tests/e2e',
	// Files run in parallel across workers, but tests within a file run sequentially.
	// This ensures each file's beforeAll runs exactly once, preventing concurrent
	// attempts to create the same seed data in the shared SQLite backend.
	fullyParallel: false,
	workers: 4,
	// Fail the build on CI if a test is accidentally left with `.only`.
	forbidOnly: !!process.env.CI,
	retries: process.env.CI ? 2 : 0,
	reporter: 'html',
	globalTeardown: './tests/e2e/global-teardown.ts',

	use: {
		baseURL: E2E_FRONTEND_URL,
		trace: 'on-first-retry',
	},

	projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

	webServer: [
		{
			// Dedicated Vite dev server for E2E — proxies to the E2E backend port.
			command: `VITE_BACKEND_URL=${E2E_BACKEND_URL} pnpm dev --port ${E2E_FRONTEND_PORT}`,
			url: E2E_FRONTEND_URL,
			reuseExistingServer: false,
		},
		{
			// Dedicated backend for E2E — always fresh, isolated temp database.
			command: `dotnet run --project ../backend/src --urls ${E2E_BACKEND_URL}`,
			url: `${E2E_BACKEND_URL}/health`,
			reuseExistingServer: false,
			timeout: 120_000,
			env: {
				ASPNETCORE_ENVIRONMENT: 'Development',
				Database__ConnectionString: `Data Source=${e2eDbPath}`,
				Seed__AdminUsername: 'admin',
				Seed__AdminPassword: 'Admin1234',
			},
		},
	],
});
