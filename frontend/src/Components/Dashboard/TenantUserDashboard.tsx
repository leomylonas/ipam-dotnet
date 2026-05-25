import { Tag } from '@carbon/react';
import { useDashboard } from '../../Hooks/useDashboard';
import { MetricCard } from '../Metric/MetricCard';
import { MetricGrid } from '../Metric/MetricGrid';
import { Section } from '../Section/Section';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { IpamDataTable, type ColumnDef } from '../DataTable/IpamDataTable';
import type { RecentAllocation, AccessibleSubnet } from '../../Services/DashboardService';
import { formatTs } from '../../Utils/DashboardUtils';

const accessibleSubnetColumns: ColumnDef<AccessibleSubnet>[] = [
	{ key: 'cidr', header: 'Subnet', render: (r) => r.cidr },
	{ key: 'freeIps', header: 'Free IPs', render: (r) => r.freeIps.toLocaleString() },
];

const recentAllocationColumns: ColumnDef<RecentAllocation>[] = [
	{ key: 'ipAddress', header: 'IP Address', render: (r) => r.ipAddress },
	{ key: 'subnetCidr', header: 'Subnet', render: (r) => r.subnetCidr },
	{ key: 'allocatedAt', header: 'Allocated', render: (r) => formatTs(r.allocatedAt) },
	{
		key: 'tags',
		header: 'Tags',
		render: (r) =>
			Object.entries(r.tags).map(([k, v]) => (
				<Tag key={k} type="blue" size="sm">
					{k}: {v}
				</Tag>
			)),
	},
];

export function TenantUserDashboard() {
	const { useUser } = useDashboard();
	const { data, isLoading, error } = useUser(true);

	return (
		<>
			<ErrorBanner error={error} title="Failed to load dashboard" />
			<MetricGrid>
				<MetricCard label="Accessible Subnets" value={data?.accessibleSubnets.length ?? '—'} />
				<MetricCard label="Recent Allocations" value={data?.recentAccessibleAllocations.length ?? '—'} />
			</MetricGrid>
			<Section title="Accessible Subnets">
				<IpamDataTable
					columns={accessibleSubnetColumns}
					rows={(data?.accessibleSubnets ?? []).map((s) => ({ ...s, id: s.subnetId }))}
					isLoading={isLoading}
					emptyMessage="You do not have access to any subnets."
				/>
			</Section>
			<Section title="Recent Allocations">
				<IpamDataTable
					columns={recentAllocationColumns}
					rows={(data?.recentAccessibleAllocations ?? []).map((a) => ({ ...a, id: a.id }))}
					isLoading={isLoading}
					emptyMessage="No recent allocations."
				/>
			</Section>
		</>
	);
}
