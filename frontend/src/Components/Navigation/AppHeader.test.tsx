import { describe, it, expect, vi, beforeEach } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../../tests/render';
import { AppHeader } from './AppHeader';

// ── Mocks ─────────────────────────────────────────────────────────────────────

// ThemeSwitcher has its own test suite; stub it here so AppHeader tests stay
// focused on the header's own behaviour.
vi.mock('./ThemeSwitcher', () => ({
	ThemeSwitcher: () => null,
}));

// ── Tests ─────────────────────────────────────────────────────────────────────

beforeEach(() => {
	vi.clearAllMocks();
});

describe('AppHeader', () => {
	it('renders the IPAM brand name', () => {
		render(<AppHeader sideNavOpen={false} onMenuToggle={vi.fn()} onLogout={vi.fn()} />);
		expect(screen.getByText('IPAM')).toBeInTheDocument();
	});

	it('labels the menu button "Open menu" when the nav is closed', () => {
		render(<AppHeader sideNavOpen={false} onMenuToggle={vi.fn()} onLogout={vi.fn()} />);
		expect(screen.getByTestId('menu-toggle')).toHaveAttribute('aria-label', 'Open menu');
	});

	it('labels the menu button "Close menu" when the nav is open', () => {
		render(<AppHeader sideNavOpen={true} onMenuToggle={vi.fn()} onLogout={vi.fn()} />);
		expect(screen.getByTestId('menu-toggle')).toHaveAttribute('aria-label', 'Close menu');
	});

	it('calls onMenuToggle when the menu button is clicked', async () => {
		const onMenuToggle = vi.fn();
		const user = userEvent.setup();
		render(<AppHeader sideNavOpen={false} onMenuToggle={onMenuToggle} onLogout={vi.fn()} />);
		await user.click(screen.getByTestId('menu-toggle'));
		expect(onMenuToggle).toHaveBeenCalledOnce();
	});

	it('calls onLogout when the logout button is clicked', async () => {
		const onLogout = vi.fn();
		const user = userEvent.setup();
		render(<AppHeader sideNavOpen={false} onMenuToggle={vi.fn()} onLogout={onLogout} />);
		await user.click(screen.getByTestId('logout-button'));
		expect(onLogout).toHaveBeenCalledOnce();
	});
});
