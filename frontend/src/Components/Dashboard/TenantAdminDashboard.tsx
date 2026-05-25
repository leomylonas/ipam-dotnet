import { Tag } from '@carbon/react';
import { useDashboard } from '../../Hooks/useDashboard';
import { MetricCard } from '../Metric/MetricCard';
import { MetricGrid } from '../Metric/MetricGrid';
import { Section } from '../Section/Section';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { IpamDataTable, type ColumnDef } from '../DataTable/IpamDataTable';
import { CopyableId } from '../CopyableId/CopyableId';
import type { TenantDashboardAuditEntry, TenantExhaustionAlert } from '../../Services/DashboardService';
import { formatTs, utilisationIntent } from '../../Utils/DashboardUtils';
import styles from './TenantAdminDashboard.module.scss';

const alertColumns: ColumnDef<TenantExhaustionAlert>[] = [
	{ key: 'cidr', header: 'Subnet', render: (r) => r.cidr },
	{
		key: 'utilisation',
		header: 'Utilisation',
		render: (r) => (
			<Tag type={r.utilisationPercent >= 90 ? 'red' : 'warm-gray'}>{r.utilisationPercent.toFixed(1)}%</Tag>
		),
	},
];

const auditColumns: ColumnDef<TenantDashboardAuditEntry>[] = [
	{ key: 'timestamp', header: 'Time', render: (r) => formatTs(r.timestamp) },
	{ key: 'action', header: 'Action', render: (r) => r.action },
	{ key: 'performedBy', header: 'User', render: (r) => <CopyableId label={r.performedBy} fullId={r.userId} /> },
	{ key: 'detail', header: 'Detail', render: (r) => r.detail ?? '—' },
];

export function TenantAdminDashboard() {
	const { useTenant } = useDashboard();
	const { data, isLoading, error } = useTenant(true);

	return (
		<>
			<ErrorBanner error={error} title="Failed to load dashboard" />
			{data !== undefined && (
				<p className={styles.tenancyBadge}>
					Tenancy: <strong>{data.tenancyName}</strong>
				</p>
			)}
			<MetricGrid>
				<MetricCard label="Users" value={data?.userCount ?? '—'} />
				<MetricCard label="Private Subnets" value={data?.privateSubnetCount ?? '—'} />
				<MetricCard label="Shared Subnets (accessible)" value={data?.accessibleSharedSubnetCount ?? '—'} />
				{data !== undefined && (
					<MetricCard
						label="Private Utilisation"
						value={data.privateSubnetUtilisation.utilisationPercent.toFixed(1)}
						unit="%"
						intent={utilisationIntent(data.privateSubnetUtilisation.utilisationPercent)}
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
