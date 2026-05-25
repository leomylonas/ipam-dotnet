import { render as rtlRender, type RenderOptions } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from '../src/Components/ThemeProvider';

// Wrap every render call in the providers that most components need.
// Tests that require additional context (e.g. a router) should pass a custom
// wrapper via RenderOptions.
function AllProviders({ children }: { children: React.ReactNode }) {
	// Fresh QueryClient per render — no shared state between tests.
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	return (
		<QueryClientProvider client={queryClient}>
			<ThemeProvider>{children}</ThemeProvider>
		</QueryClientProvider>
	);
}

export function render(ui: React.ReactElement, options?: Omit<RenderOptions, 'wrapper'>) {
	return rtlRender(ui, { wrapper: AllProviders, ...options });
}

// Re-export everything else from Testing Library so tests only need one import.
export * from '@testing-library/react';
