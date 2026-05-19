import {
	Header,
	HeaderGlobalAction,
	HeaderGlobalBar,
	HeaderMenuButton,
	HeaderName,
	SkipToContent,
} from '@carbon/react';
import { Logout } from '@carbon/icons-react';
import { ThemeSwitcher } from './ThemeSwitcher';

interface AppHeaderProps {
	/** Whether the side navigation overlay is currently open. */
	sideNavOpen: boolean;
	/** Called when the hamburger menu button is clicked. */
	onMenuToggle: () => void;
	/** Called when the user clicks the log out action. */
	onLogout: () => void;
}

/** The top application header bar with branding, menu toggle, theme switcher, and logout. */
export function AppHeader({ sideNavOpen, onMenuToggle, onLogout }: AppHeaderProps) {
	return (
		<Header aria-label="IPAM — IP Address Management">
			<SkipToContent />
			<HeaderMenuButton
				aria-label={sideNavOpen ? 'Close menu' : 'Open menu'}
				data-testid="menu-toggle"
				isActive={sideNavOpen}
				onClick={onMenuToggle}
			/>
			<HeaderName href="/" prefix="">
				IPAM
			</HeaderName>
			<HeaderGlobalBar>
				<ThemeSwitcher />
				<HeaderGlobalAction
					aria-label="Log out"
					data-testid="logout-button"
					tooltipAlignment="end"
					onClick={onLogout}
				>
					<Logout />
				</HeaderGlobalAction>
			</HeaderGlobalBar>
		</Header>
	);
}
