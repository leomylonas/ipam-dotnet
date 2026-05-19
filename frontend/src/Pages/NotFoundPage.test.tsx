import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '../../tests/render';
import { NotFoundPage } from './NotFoundPage';

// ── Mocks ─────────────────────────────────────────────────────────────────────

// TanStack Router's Link renders an <a> that requires a router context. Swap
// it for a plain anchor so the test doesn't need to stand up a full router.
vi.mock('@tanstack/react-router', () => ({
	Link: ({ to, children, className }: { to: string; children: React.ReactNode; className?: string }) => (
		<a href={to} className={className}>
			{children}
		</a>
	),
}));

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('NotFoundPage', () => {
	it('renders the 404 heading', () => {
		render(<NotFoundPage />);
		expect(screen.getByRole('heading', { name: '404' })).toBeInTheDocument();
	});

	it('displays a descriptive message', () => {
		render(<NotFoundPage />);
		expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
	});

	it('has a link back to the dashboard', () => {
		render(<NotFoundPage />);
		const link = screen.getByRole('link', { name: 'Go to Dashboard' });
		expect(link).toHaveAttribute('href', '/');
	});
});
