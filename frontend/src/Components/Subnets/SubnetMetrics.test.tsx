import { describe, it, expect } from 'vitest';
import { render, screen } from '../../../tests/render';
import { SubnetMetrics } from './SubnetMetrics';
import type { SubnetStats } from '../../Services/StatsService';

const fullStats: SubnetStats = {
	subnetId: 'subnet-1',
	totalIps: 254,
	allocatedCount: 100,
	freeCount: 104,
	excludedCount: 50,
};

describe('SubnetMetrics', () => {
	it('shows dash placeholders when stats is undefined', () => {
		render(<SubnetMetrics stats={undefined} />);
		const dashes = screen.getAllByText('—');
		expect(dashes.length).toBeGreaterThanOrEqual(4);
	});

	it('renders Total IPs from stats', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('254')).toBeInTheDocument();
	});

	it('renders Allocated count from stats', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('100')).toBeInTheDocument();
	});

	it('renders Free count from stats', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('104')).toBeInTheDocument();
	});

	it('renders Excluded count from stats', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('50')).toBeInTheDocument();
	});

	it('renders the Utilisation card when stats are available', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('Utilisation')).toBeInTheDocument();
	});

	it('does not render Utilisation card when stats is undefined', () => {
		render(<SubnetMetrics stats={undefined} />);
		expect(screen.queryByText('Utilisation')).not.toBeInTheDocument();
	});

	it('does not render Utilisation card when totalIps is zero', () => {
		const zeroStats: SubnetStats = { ...fullStats, totalIps: 0, allocatedCount: 0, freeCount: 0, excludedCount: 0 };
		render(<SubnetMetrics stats={zeroStats} />);
		expect(screen.queryByText('Utilisation')).not.toBeInTheDocument();
	});

	it('shows correct utilisation percentage', () => {
		// (100 allocated + 50 excluded) / 254 total ≈ 59.1%
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('59.1')).toBeInTheDocument();
	});

	it('shows all four metric labels', () => {
		render(<SubnetMetrics stats={fullStats} />);
		expect(screen.getByText('Total IPs')).toBeInTheDocument();
		expect(screen.getByText('Allocated')).toBeInTheDocument();
		expect(screen.getByText('Free')).toBeInTheDocument();
		expect(screen.getByText('Excluded')).toBeInTheDocument();
	});
});
