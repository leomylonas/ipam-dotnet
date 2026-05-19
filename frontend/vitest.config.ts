import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

// Extend the existing Vite config so the SCSS preprocessor options, plugins,
// and aliases are shared without duplication. The proxy server config is
// irrelevant inside the test runner and is harmlessly ignored.
export default mergeConfig(
	viteConfig,
	defineConfig({
		test: {
			environment: 'jsdom',
			setupFiles: ['./tests/setup.ts'],
			include: ['src/**/*.test.{ts,tsx}'],
		},
	}),
);
