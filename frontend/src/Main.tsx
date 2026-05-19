import './Styles/Main.scss';
import { StrictMode, useEffect, useMemo } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from '@tanstack/react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from './Components/ThemeProvider';
import { useAuthStore } from './Stores/AuthStore';
import { router } from './Router';
import { useAuthService } from './Services/AuthService';

// ── Root component ────────────────────────────────────────────────────────────

/**
 * Application root. Responsible for:
 * 1. Checking whether an existing auth cookie is valid (GET /auth/me on mount).
 * 2. Writing the resolved user (or null) into the global auth store.
 * 3. Clearing the isCheckingAuth flag once the check settles so the router
 *    renders only after the auth state is definitively known.
 */
function Root() {
	// Subscribe to the loading flag so this component re-renders once the
	// startup auth check completes and the full app tree can mount.
	const { setUser, isCheckingAuth, setIsCheckingAuth } = useAuthStore();

	const authService = useAuthService();

	// Stable QueryClient instance for the app
	const queryClient = useMemo(() => new QueryClient(), []);

	useEffect(() => {
		// Attempt to restore an existing session from the auth cookie. A 401
		// response (no valid cookie) is expected and intentional — it means the
		// user is unauthenticated and will be sent to the login page.
		authService
			.me()
			.then(setUser)
			.catch(() => {
				// Leave user as null — the router guard will redirect to /login.
			})
			.finally(() => {
				// Clear the flag last so user is set before the router evaluates
				// the beforeLoad guard on the first render.
				setIsCheckingAuth(false);
			});
		// authService, setUser, and setIsCheckingAuth are all stable references
		// (useMemo / useStoreUpdate singletons) so this runs exactly once on mount.
	}, [authService, setIsCheckingAuth, setUser]);

	// Hold rendering until the auth check resolves so the route tree does not
	// flash the wrong page. A loading indicator is intentionally omitted —
	// the blank flash is imperceptible for a local network call.
	if (isCheckingAuth) {
		return null;
	}

	return (
		<ThemeProvider>
			<QueryClientProvider client={queryClient}>
				<RouterProvider router={router} />
			</QueryClientProvider>
		</ThemeProvider>
	);
}

// ── Entry point ───────────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
createRoot(document.getElementById('root')!).render(
	<StrictMode>
		<Root />
	</StrictMode>,
);
