import { useAudit } from '../../Hooks/useAudit';
import { useUsers } from '../../Hooks/useUsers';
import { useSubnets } from '../../Hooks/useSubnets';
import { useAuthStore } from '../../Stores/AuthStore';
import { type AuditLog } from '../../Services/AuditService';
import { IpamDataTable, type ColumnDef } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { CopyableId } from '../CopyableId/CopyableId';

export function AuditList() {
	const { user } = useAuthStore();

	const { useAuditQuery } = useAudit();
	const { useUsersQuery } = useUsers();
	const { useSharedQuery, usePrivateQuery } = useSubnets();

	const { data, isLoading, error } = useAuditQuery();
	const { data: users } = useUsersQuery();
	const { data: sharedSubnets } = useSharedQuery();
	// Private subnets are scoped to a tenancy — GlobalAdmin has no tenancyId so this is disabled.
	const { data: privateSubnets } = usePrivateQuery(user?.tenancyId ?? '');

	// Build lookup maps so cell renderers can resolve IDs in O(1).
	const userMap = new Map((users ?? []).map((u) => [u.id, u.username]));
	const subnetMap = new Map(
		[...(sharedSubnets ?? []), ...(privateSubnets ?? [])].map((s) => [s.id, `${s.cidr} — ${s.name}`]),
	);

	// Build resolved columns with access to the maps
	const resolvedColumns: ColumnDef<AuditLog>[] = [
		{
			key: 'timestamp',
			header: 'Timestamp',
			render: (e) => new Date(e.timestamp).toLocaleString(),
			sortValue: (e) => new Date(e.timestamp).getTime(),
		},
		{ key: 'action', header: 'Action', render: (e) => e.action, sortValue: (e) => e.action },
		{
			key: 'userId',
			header: 'User',
			render: (e) => {
				const name = userMap.get(e.userId);
				return <CopyableId label={name ?? `${e.userId.slice(0, 8)}…`} fullId={e.userId} />;
			},
			sortValue: (e) => userMap.get(e.userId) ?? e.userId,
		},
		{
			key: 'ipAddress',
			header: 'IP Address',
			render: (e) => e.ipAddress ?? '—',
			sortValue: (e) => e.ipAddress ?? '',
		},
		{
			key: 'subnetId',
			header: 'Subnet',
			render: (e) => {
				if (e.subnetId === null) {
					return '—';
				}
				const name = subnetMap.get(e.subnetId);
				return <CopyableId label={name ?? `${e.subnetId.slice(0, 8)}…`} fullId={e.subnetId} />;
			},
			sortValue: (e) => (e.subnetId !== null ? (subnetMap.get(e.subnetId) ?? e.subnetId) : ''),
		},
		{ key: 'notes', header: 'Notes', render: (e) => e.notes ?? '—' },
	];

	return (
		<>
			<ErrorBanner error={error} title="Failed to load audit log" />
			<IpamDataTable
				columns={resolvedColumns}
				rows={data ?? []}
				isLoading={isLoading}
				emptyMessage="No audit log entries found."
			/>
		</>
	);
}
