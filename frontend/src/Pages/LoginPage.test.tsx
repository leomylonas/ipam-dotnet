import { describe, it, expect, vi, beforeEach } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../tests/render';
import { LoginPage } from './LoginPage';
import type * as AuthServiceModule from '../Services/AuthService';

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock('@tanstack/react-router', () => ({
	useNavigate: () => vi.fn(),
}));

// Keep the real loginRequestSchema (used by the form's Zod resolver) but
// replace the hook so tests don't make real HTTP requests.
const mockLogin = vi.fn();
vi.mock('../Services/AuthService', async (importOriginal) => {
	const mod = await importOriginal<typeof AuthServiceModule>();
	return { ...mod, useAuthService: () => ({ login: mockLogin }) };
});

vi.mock('../Stores/AuthStore', () => ({
	useAuthStore: () => ({ setUser: vi.fn() }),
}));

// ── Tests ─────────────────────────────────────────────────────────────────────

beforeEach(() => {
	mockLogin.mockReset();
});

describe('LoginPage', () => {
	it('renders username and password fields with a submit button', () => {
		render(<LoginPage />);
		expect(screen.getByLabelText('Username')).toBeInTheDocument();
		expect(screen.getByLabelText('Password')).toBeInTheDocument();
		expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
	});

	it('shows inline validation errors on empty submit without calling the API', async () => {
		const user = userEvent.setup();
		render(<LoginPage />);

		await user.click(screen.getByRole('button', { name: /sign in/i }));

		expect(await screen.findByText(/username is required/i)).toBeInTheDocument();
		expect(await screen.findByText(/password is required/i)).toBeInTheDocument();
		expect(mockLogin).not.toHaveBeenCalled();
	});

	it('shows the error banner when the API rejects the credentials', async () => {
		mockLogin.mockRejectedValue(new Error('Unauthorized'));
		const user = userEvent.setup();
		render(<LoginPage />);

		await user.type(screen.getByLabelText('Username'), 'admin');
		await user.type(screen.getByLabelText('Password'), 'wrong');
		await user.click(screen.getByRole('button', { name: /sign in/i }));

		expect(await screen.findByText(/invalid username or password/i)).toBeInTheDocument();
	});

	it('calls login with the entered credentials', async () => {
		mockLogin.mockResolvedValue({ id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null });
		const user = userEvent.setup();
		render(<LoginPage />);

		await user.type(screen.getByLabelText('Username'), 'admin');
		await user.type(screen.getByLabelText('Password'), 'Admin1234');
		await user.click(screen.getByRole('button', { name: /sign in/i }));

		expect(mockLogin).toHaveBeenCalledWith({ username: 'admin', password: 'Admin1234' });
	});
});
