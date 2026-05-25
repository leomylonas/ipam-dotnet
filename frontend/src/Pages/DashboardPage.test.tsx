import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '../../tests/render';
import { DashboardPage } from './DashboardPage';
import type { AuthResponse } from '../Services/AuthService';

// ── Mocks ─────────────────────────────────────────────────────────────────────

const mockUseAuthStore = vi.hoisted(() => vi.fn());
vi.mock('../Stores/AuthStore', () => ({
	useAuthStore: mockUseAuthStore,
}));

// Mock dashboard sub-components to avoid triggering API queries in unit tests.
vi.mock('../Components/Dashboard/GlobalAdminDashboard', () => ({
	GlobalAdminDashboard: () => <div data-testid="global-admin-dashboard" />,
}));
vi.mock('../Components/Dashboard/TenantAdminDashboard', () => ({
	TenantAdminDashboard: () => <div data-testid="tenant-admin-dashboard" />,
}));
vi.mock('../Components/Dashboard/TenantUserDashboard', () => ({
	TenantUserDashboard: () => <div data-testid="tenant-user-dashboard" />,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderAs(user: AuthResponse | null) {
	mockUseAuthStore.mockReturnValue({ user });
	return render(<DashboardPage />);
}

const globalAdmin: AuthResponse = { id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null };
const tenantAdmin: AuthResponse = {
	id: '2',
	username: 'tadmin',
	role: 'TenantAdmin',
	tenancyId: 'a1b2c3d4-0000-0000-0000-000000000001',
};
const tenantUser: AuthResponse = {
	id: '3',
	username: 'tuser',
	role: 'TenantUser',
	tenancyId: 'a1b2c3d4-0000-0000-0000-000000000001',
};

// ── Tests ─────────────────────────────────────────────────────────────────────

beforeEach(() => {
	mockUseAuthStore.mockReset();
});

describe('DashboardPage', () => {
	it('renders nothing when user is null', () => {
		renderAs(null);
		expect(screen.queryByRole('heading', { name: 'Dashboard' })).not.toBeInTheDocument();
	});

	it('shows the Dashboard heading', () => {
		renderAs(globalAdmin);
		expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
	});

	it('renders GlobalAdminDashboard for GlobalAdmin role', () => {
		renderAs(globalAdmin);
		expect(screen.getByTestId('global-admin-dashboard')).toBeInTheDocument();
		expect(screen.queryByTestId('tenant-admin-dashboard')).not.toBeInTheDocument();
		expect(screen.queryByTestId('tenant-user-dashboard')).not.toBeInTheDocument();
	});

	it('renders TenantAdminDashboard for TenantAdmin role', () => {
		renderAs(tenantAdmin);
		expect(screen.getByTestId('tenant-admin-dashboard')).toBeInTheDocument();
		expect(screen.queryByTestId('global-admin-dashboard')).not.toBeInTheDocument();
		expect(screen.queryByTestId('tenant-user-dashboard')).not.toBeInTheDocument();
	});

	it('renders TenantUserDashboard for TenantUser role', () => {
		renderAs(tenantUser);
		expect(screen.getByTestId('tenant-user-dashboard')).toBeInTheDocument();
		expect(screen.queryByTestId('global-admin-dashboard')).not.toBeInTheDocument();
		expect(screen.queryByTestId('tenant-admin-dashboard')).not.toBeInTheDocument();
	});

	it('renders exactly one dashboard component per role', () => {
		const cases: [AuthResponse, string][] = [
			[globalAdmin, 'global-admin-dashboard'],
			[tenantAdmin, 'tenant-admin-dashboard'],
			[tenantUser, 'tenant-user-dashboard'],
		];
		for (const [user, testId] of cases) {
			mockUseAuthStore.mockReturnValue({ user });
			const { unmount } = render(<DashboardPage />);
			expect(screen.getByTestId(testId)).toBeInTheDocument();
			unmount();
		}
	});
});
