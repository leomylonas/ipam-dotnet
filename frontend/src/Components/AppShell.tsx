import { useState } from 'react';
import { Outlet, useNavigate } from '@tanstack/react-router';
import { Content, Section } from '@carbon/react';
import styles from './AppShell.module.scss';
import { useAuthService } from '../Services/AuthService';
import { useAuthStore } from '../Stores/AuthStore';
import { AppHeader } from './Navigation/AppHeader';
import { AppSideNav } from './Navigation/AppSideNav';

/**
 * The authenticated application shell. Coordinates the header, side navigation,
 * and main content area. Holds the mobile nav open/close state and logout logic
 * that both the header and sidenav depend on.
 */
export function AppShell() {
	const { setUser } = useAuthStore();
	const authService = useAuthService();
	const navigate = useNavigate();
	const [sideNavOpen, setSideNavOpen] = useState(true);

	// Sign out: clear cookie server-side, reset local auth state, and redirect
	// to /login. Errors are silently swallowed — the redirect happens regardless
	// so the UI always ends up in a clean unauthenticated state.
	const handleLogout = () => {
		void authService
			.logout()
			.catch(() => undefined)
			.finally(() => {
				setUser(null);
				void navigate({ to: '/login' });
			});
	};

	return (
		<>
			<AppHeader
				sideNavOpen={sideNavOpen}
				onMenuToggle={() => {
					setSideNavOpen((prev) => !prev);
				}}
				onLogout={handleLogout}
			/>
			<AppSideNav
				expanded={sideNavOpen}
				onToggle={() => {
					setSideNavOpen((prev) => !prev);
				}}
			/>

			{/* Main content area — Section bumps the HeadingContext from 1 to 2
			    so that every page's first Heading renders as h2. */}
			<Content id="main-content" className={styles.content}>
				<Section>
					<Outlet />
				</Section>
			</Content>
		</>
	);
}
