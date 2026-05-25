import { Tag } from '@carbon/react';
import { useDashboard } from '../../Hooks/useDashboard';
import { MetricCard } from '../Metric/MetricCard';
import { MetricGrid } from '../Metric/MetricGrid';
import { Section } from '../Section/Section';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { IpamDataTable, type ColumnDef } from '../DataTable/IpamDataTable';
import { CopyableId } from '../CopyableId/CopyableId';
import type { GlobalDashboardAuditEntry, GlobalExhaustionAlert } from '../../Services/DashboardService';
import { formatTs, utilisationIntent } from '../../Utils/DashboardUtils';

const alertColumns: ColumnDef<GlobalExhaustionAlert>[] = [
	{ key: 'cidr', header: 'Subnet', render: (r) => r.cidr },
	{ key: 'tenancyName', header: 'Tenancy', render: (r) => r.tenancyName ?? '(shared)' },
	{
		key: 'utilisation',
		header: 'Utilisation',
		render: (r) => (
			<Tag type={r.utilisationPercent >= 90 ? 'red' : 'warm-gray'}>{r.utilisationPercent.toFixed(1)}%</Tag>
		),
	},
];

const auditColumns: ColumnDef<GlobalDashboardAuditEntry>[] = [
	{ key: 'timestamp', header: 'Time', render: (r) => formatTs(r.timestamp) },
	{ key: 'action', header: 'Action', render: (r) => r.action },
	{ key: 'performedBy', header: 'User', render: (r) => <CopyableId label={r.performedBy} fullId={r.userId} /> },
	{
		key: 'tenancyName',
		header: 'Tenancy',
		render: (r) =>
			r.tenancyId !== null && r.tenancyName !== null ? (
				<CopyableId label={r.tenancyName} fullId={r.tenancyId} />
			) : (
				'—'
			),
	},
	{ key: 'detail', header: 'Detail', render: (r) => r.detail ?? '—' },
];

export function GlobalAdminDashboard() {
	const { useGlobal } = useDashboard();
	const { data, isLoading, error } = useGlobal(true);

	return (
		<>
			<ErrorBanner error={error} title="Failed to load dashboard" />
			<MetricGrid>
				<MetricCard label="Tenancies" value={data?.tenancyCount ?? '—'} />
				<MetricCard label="Users" value={data?.userCount ?? '—'} />
				<MetricCard label="Shared Subnets" value={data?.sharedSubnetCount ?? '—'} />
				{data !== undefined && (
					<MetricCard
						label="Shared Utilisation"
						value={data.sharedSubnetUtilisation.utilisationPercent.toFixed(1)}
						unit="%"
						intent={utilisationIntent(data.sharedSubnetUtilisation.utilisationPercent)}
					/>
				)}
			</MetricGrid>
			{data !== undefined && data.subnetsApproachingExhaustion.length > 0 && (
				<Section title="Subnets Approaching Exhaustion">
					<IpamDataTable
						columns={alertColumns}
						rows={data.subnetsApproachingExhaustion.map((a) => ({ ...a, id: a.subnetId }))}
					/>
				</Section>
			)}
			<Section title="Recent Activity">
				<IpamDataTable
					columns={auditColumns}
					rows={(data?.recentAuditEntries ?? []).map((e) => ({ ...e, id: e.id }))}
					isLoading={isLoading}
					emptyMessage="No recent activity."
				/>
			</Section>
		</>
	);
}
