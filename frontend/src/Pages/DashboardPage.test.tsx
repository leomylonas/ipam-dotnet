import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '../../tests/render';
import { DashboardPage } from './DashboardPage';
import type { AuthResponse } from '../Services/AuthService';

// ── Mocks ─────────────────────────────────────────────────────────────────────

const mockUseAuthStore = vi.hoisted(() => vi.fn());
vi.mock('../Stores/AuthStore', () => ({
	useAuthStore: mockUseAuthStore,
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

	it('displays the signed-in username', () => {
		renderAs(globalAdmin);
		expect(screen.getByText('admin')).toBeInTheDocument();
	});

	it('displays the human-readable role label, not the raw role string', () => {
		renderAs(globalAdmin);
		// Should show "Global Admin", not "GlobalAdmin".
		expect(screen.getByText('Global Admin')).toBeInTheDocument();
		expect(screen.queryByText('GlobalAdmin')).not.toBeInTheDocument();
	});

	it('shows correct role labels for all three roles', () => {
		const cases: [AuthResponse, string][] = [
			[globalAdmin, 'Global Admin'],
			[tenantAdmin, 'Tenant Admin'],
			[tenantUser, 'Tenant User'],
		];
		for (const [user, label] of cases) {
			mockUseAuthStore.mockReturnValue({ user });
			const { unmount } = render(<DashboardPage />);
			expect(screen.getByText(label)).toBeInTheDocument();
			unmount();
		}
	});

	it('hides the tenancy row for GlobalAdmin (tenancyId is null)', () => {
		renderAs(globalAdmin);
		expect(screen.queryByText('Tenancy')).not.toBeInTheDocument();
	});

	it('shows the tenancyId for TenantAdmin', () => {
		renderAs(tenantAdmin);
		expect(screen.getByText('a1b2c3d4-0000-0000-0000-000000000001')).toBeInTheDocument();
	});

	it('shows the tenancyId for TenantUser', () => {
		renderAs(tenantUser);
		expect(screen.getByText('a1b2c3d4-0000-0000-0000-000000000001')).toBeInTheDocument();
	});
});
