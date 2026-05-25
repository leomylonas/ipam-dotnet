import { MetricCard } from '../Metric/MetricCard';
import { MetricGrid } from '../Metric/MetricGrid';
import type { SubnetStats } from '../../Services/StatsService';

interface SubnetMetricsProps {
	stats: SubnetStats | undefined;
}

export function SubnetMetrics({ stats }: SubnetMetricsProps) {
	const utilisation =
		stats !== undefined && stats.totalIps > 0
			? (stats.allocatedCount + stats.excludedCount) / stats.totalIps
			: null;

	return (
		<MetricGrid>
			<MetricCard label="Total IPs" value={stats?.totalIps ?? '—'} />
			<MetricCard label="Allocated" value={stats?.allocatedCount ?? '—'} />
			<MetricCard label="Free" value={stats?.freeCount ?? '—'} intent="success" />
			<MetricCard label="Excluded" value={stats?.excludedCount ?? '—'} />
			{utilisation !== null && (
				<MetricCard
					label="Utilisation"
					value={(utilisation * 100).toFixed(1)}
					unit="%"
					intent={utilisation >= 0.9 ? 'error' : utilisation >= 0.8 ? 'warning' : 'success'}
				/>
			)}
		</MetricGrid>
	);
}
