import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

// @testing-library/react auto-cleanup requires afterEach as a global.
// Vitest doesn't expose globals by default, so register explicitly.
afterEach(cleanup);

// Carbon components and ThemeProvider use matchMedia for prefers-color-scheme.
// jsdom doesn't implement it, so provide a stub that defaults to light mode.
Object.defineProperty(window, 'matchMedia', {
	writable: true,
	value: vi.fn().mockImplementation((query: string) => ({
		matches: false,
		media: query,
		onchange: null,
		addEventListener: vi.fn(),
		removeEventListener: vi.fn(),
		dispatchEvent: vi.fn(),
	})),
});

// Carbon components use ResizeObserver for responsive layout; jsdom has no
// native implementation so provide a no-op stub.
// eslint-disable-next-line @typescript-eslint/no-empty-function
const noop = () => {};
globalThis.ResizeObserver = class ResizeObserver {
	observe = noop;
	unobserve = noop;
	disconnect = noop;
};
