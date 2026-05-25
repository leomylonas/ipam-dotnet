import { describe, it, expect } from 'vitest';
import { render, screen } from '../../../tests/render';
import { MetricCard } from './MetricCard';

describe('MetricCard', () => {
	it('renders the label', () => {
		render(<MetricCard label="Total IPs" value={254} />);
		expect(screen.getByText('Total IPs')).toBeInTheDocument();
	});

	it('renders a numeric value', () => {
		render(<MetricCard label="Total IPs" value={254} />);
		expect(screen.getByText('254')).toBeInTheDocument();
	});

	it('renders a string value', () => {
		render(<MetricCard label="Utilisation" value="59.1" />);
		expect(screen.getByText('59.1')).toBeInTheDocument();
	});

	it('renders the unit when provided', () => {
		render(<MetricCard label="Utilisation" value="59.1" unit="%" />);
		expect(screen.getByText('%')).toBeInTheDocument();
	});

	it('does not render a unit element when omitted', () => {
		render(<MetricCard label="Total IPs" value={254} />);
		// No stray "%" or similar text.
		expect(screen.queryByText('%')).not.toBeInTheDocument();
	});

	it('renders the dash placeholder value "—" correctly', () => {
		render(<MetricCard label="Total IPs" value="—" />);
		expect(screen.getByText('—')).toBeInTheDocument();
	});
});
