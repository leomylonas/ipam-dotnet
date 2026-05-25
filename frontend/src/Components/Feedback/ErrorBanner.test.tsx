import { describe, it, expect } from 'vitest';
import { render, screen } from '../../../tests/render';
import { ErrorBanner } from './ErrorBanner';

describe('ErrorBanner', () => {
	it('renders nothing when error is null', () => {
		render(<ErrorBanner error={null} title="Oops" />);
		expect(screen.queryByRole('alert')).not.toBeInTheDocument();
	});

	it('renders nothing when error is undefined', () => {
		render(<ErrorBanner error={undefined} title="Oops" />);
		expect(screen.queryByRole('alert')).not.toBeInTheDocument();
	});

	it('shows the notification when a plain Error is passed', () => {
		// Carbon InlineNotification renders with role="status", not role="alert".
		render(<ErrorBanner error={new Error('Something broke')} title="Oops" />);
		expect(screen.getByRole('status')).toBeInTheDocument();
	});

	it('displays the error message from a plain Error', () => {
		render(<ErrorBanner error={new Error('Something broke')} title="Oops" />);
		expect(screen.getByText('Something broke')).toBeInTheDocument();
	});

	it('uses the provided title', () => {
		render(<ErrorBanner error={new Error('fail')} title="Failed to load data" />);
		expect(screen.getByText('Failed to load data')).toBeInTheDocument();
	});

	it('uses "Error" as the default title', () => {
		render(<ErrorBanner error={new Error('fail')} />);
		expect(screen.getByText('Error')).toBeInTheDocument();
	});

	it('shows a fallback message for non-Error objects', () => {
		render(<ErrorBanner error="a raw string error" title="Oops" />);
		expect(screen.getByText('An unexpected error occurred. Please try again.')).toBeInTheDocument();
	});
});
