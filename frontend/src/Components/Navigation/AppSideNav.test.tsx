import { describe, it, expect, vi, beforeEach } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../../tests/render';
import { AppSideNav } from './AppSideNav';

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock('@tanstack/react-router', () => ({
	useNavigate: () => vi.fn(),
	useRouterState: () => ({ location: { pathname: '/' } }),
}));

const mockUseAuthStore = vi.hoisted(() => vi.fn());
vi.mock('../../Stores/AuthStore', () => ({
	useAuthStore: mockUseAuthStore,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function mockAsRole(isGlobalAdmin: boolean, isTenantAdmin: boolean) {
	mockUseAuthStore.mockReturnValue({
		authStore: {
			isGlobalAdmin: () => isGlobalAdmin,
			isTenantAdmin: () => isTenantAdmin,
		},
	});
}

// ── Tests ─────────────────────────────────────────────────────────────────────

beforeEach(() => {
	mockUseAuthStore.mockReset();
});

describe('AppSideNav — role-based nav visibility', () => {
	describe('GlobalAdmin', () => {
		beforeEach(() => {
			mockAsRole(true, false);
		});

		it('sees all nav items', () => {
			render(<AppSideNav expanded onToggle={vi.fn()} />);
			expect(screen.getByText('Dashboard')).toBeInTheDocument();
			expect(screen.getByText('Tenancies')).toBeInTheDocument();
			expect(screen.getByText('Users')).toBeInTheDocument();
			expect(screen.getByText('Shared Subnets')).toBeInTheDocument();
			expect(screen.getByText('Subnets')).toBeInTheDocument();
			expect(screen.getByText('Allocations')).toBeInTheDocument();
			expect(screen.getByText('Audit Log')).toBeInTheDocument();
		});
	});

	describe('TenantAdmin', () => {
		beforeEach(() => {
			mockAsRole(false, true);
		});

		it('sees tenant-scoped nav items', () => {
			render(<AppSideNav expanded onToggle={vi.fn()} />);
			expect(screen.getByText('Dashboard')).toBeInTheDocument();
			expect(screen.getByText('Users')).toBeInTheDocument();
			expect(screen.getByText('Subnets')).toBeInTheDocument();
			expect(screen.getByText('Allocations')).toBeInTheDocument();
			expect(screen.getByText('Audit Log')).toBeInTheDocument();
		});

		it('does not see GlobalAdmin-only items', () => {
			render(<AppSideNav expanded onToggle={vi.fn()} />);
			expect(screen.queryByText('Tenancies')).not.toBeInTheDocument();
			expect(screen.queryByText('Shared Subnets')).not.toBeInTheDocument();
		});
	});

	describe('TenantUser', () => {
		beforeEach(() => {
			mockAsRole(false, false);
		});

		it('sees only Dashboard and Allocations', () => {
			render(<AppSideNav expanded onToggle={vi.fn()} />);
			expect(screen.getByText('Dashboard')).toBeInTheDocument();
			expect(screen.getByText('Allocations')).toBeInTheDocument();
		});

		it('does not see any admin items', () => {
			render(<AppSideNav expanded onToggle={vi.fn()} />);
			expect(screen.queryByText('Tenancies')).not.toBeInTheDocument();
			expect(screen.queryByText('Users')).not.toBeInTheDocument();
			expect(screen.queryByText('Shared Subnets')).not.toBeInTheDocument();
			expect(screen.queryByText('Subnets')).not.toBeInTheDocument();
			expect(screen.queryByText('Audit Log')).not.toBeInTheDocument();
		});
	});
});

describe('AppSideNav — collapse toggle', () => {
	beforeEach(() => {
		mockAsRole(false, false);
	});

	it('labels the toggle "Collapse navigation" when expanded', () => {
		render(<AppSideNav expanded={true} onToggle={vi.fn()} />);
		expect(screen.getByRole('button', { name: 'Collapse navigation' })).toBeInTheDocument();
	});

	it('labels the toggle "Expand navigation" when collapsed', () => {
		render(<AppSideNav expanded={false} onToggle={vi.fn()} />);
		expect(screen.getByRole('button', { name: 'Expand navigation' })).toBeInTheDocument();
	});

	it('calls onToggle when the toggle button is clicked', async () => {
		const onToggle = vi.fn();
		const user = userEvent.setup();
		render(<AppSideNav expanded={true} onToggle={onToggle} />);
		await user.click(screen.getByRole('button', { name: 'Collapse navigation' }));
		expect(onToggle).toHaveBeenCalledOnce();
	});
});
