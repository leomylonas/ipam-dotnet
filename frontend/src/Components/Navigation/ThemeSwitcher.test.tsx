import { describe, it, expect, beforeEach } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen } from '../../../tests/render';
import { ThemeSwitcher } from './ThemeSwitcher';

// ThemeSwitcher reads and writes to localStorage for theme persistence.
beforeEach(() => {
	localStorage.clear();
});

describe('ThemeSwitcher', () => {
	it('renders a theme toggle button', () => {
		render(<ThemeSwitcher />);
		expect(screen.getByRole('button', { name: /theme/i })).toBeInTheDocument();
	});

	it('opens the theme menu on click', async () => {
		const user = userEvent.setup();
		render(<ThemeSwitcher />);

		await user.click(screen.getByTestId('theme-toggle'));

		expect(screen.getByRole('button', { name: /light/i })).toBeInTheDocument();
		expect(screen.getByRole('button', { name: /dark/i })).toBeInTheDocument();
		expect(screen.getByRole('button', { name: /system/i })).toBeInTheDocument();
	});

	it('shows a checkmark next to the active mode', async () => {
		const user = userEvent.setup();
		render(<ThemeSwitcher />);
		await user.click(screen.getByTestId('theme-toggle'));

		// Default mode is 'system' — that option should contain the checkmark SVG.
		const systemButton = screen.getByRole('button', { name: /system/i });
		expect(systemButton.querySelector('svg')).toBeInTheDocument();
	});

	it('persists the selected mode to localStorage', async () => {
		const user = userEvent.setup();
		render(<ThemeSwitcher />);

		await user.click(screen.getByTestId('theme-toggle'));
		await user.click(screen.getByRole('button', { name: /dark/i }));

		expect(localStorage.getItem('ipam-theme')).toBe('dark');
	});
});
