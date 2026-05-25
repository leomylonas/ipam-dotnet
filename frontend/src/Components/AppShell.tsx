import { useEffect, useState } from 'react';
import { Outlet, useNavigate } from '@tanstack/react-router';
import { Content, Section } from '@carbon/react';
import styles from './AppShell.module.scss';
import { useAuthService } from '../Services/AuthService';
import { useAuthStore } from '../Stores/AuthStore';
import { AppHeader } from './Navigation/AppHeader';
import { AppSideNav } from './Navigation/AppSideNav';
import { useIsDesktop } from '../Utils/ReactUtils';

/**
 * The authenticated application shell. Coordinates the header, side navigation,
 * and main content area. Holds the mobile nav open/close state and logout logic
 * that both the header and sidenav depend on.
 */
export function AppShell() {
	const { setUser } = useAuthStore();
	const authService = useAuthService();
	const navigate = useNavigate();

	const isDesktop = useIsDesktop();

	// Desktop remembers the user's explicit open/close choice across breakpoint
	// changes. null = no override; defaults to open on desktop.
	const [desktopOverride, setDesktopOverride] = useState<boolean | null>(null);

	// Mobile open state is transient — resets to closed whenever the viewport
	// drops back to mobile so the user always starts with a collapsed nav.
	const [mobileOpen, setMobileOpen] = useState(false);
	useEffect(() => {
		if (!isDesktop) {
			setMobileOpen(false);
		}
	}, [isDesktop]);

	const sideNavOpen = isDesktop ? (desktopOverride ?? true) : mobileOpen;

	const handleMenuToggle = () => {
		if (isDesktop) {
			setDesktopOverride((prev) => !(prev ?? true));
		} else {
			setMobileOpen((prev) => !prev);
		}
	};

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
			<AppHeader sideNavOpen={sideNavOpen} onMenuToggle={handleMenuToggle} onLogout={handleLogout} />
			<AppSideNav expanded={sideNavOpen} onToggle={handleMenuToggle} />

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
