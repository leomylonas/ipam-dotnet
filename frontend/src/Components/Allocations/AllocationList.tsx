import { useCallback, useState } from 'react';
import { Tag, Button } from '@carbon/react';
import { useAllocations } from '../../Hooks/useAllocations';
import { useModal } from '../../Hooks/useModal';
import { useSubnets } from '../../Hooks/useSubnets';
import { useAuthStore } from '../../Stores/AuthStore';
import { type Allocation, type AllocationFilter } from '../../Services/AllocationsService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { CopyableId } from '../CopyableId/CopyableId';
import { AllocateModal } from './AllocateModal';
import { BulkAllocateModal } from './BulkAllocateModal';
import { TagsModal } from './TagsModal';
import { parseTagFilter } from '../../Utils/AllocationUtils';
import { formatTs } from '../../Utils/DashboardUtils';

export function AllocationList() {
	const { user } = useAuthStore();

	const { useSharedQuery, usePrivateQuery } = useSubnets();
	const { data: sharedSubnets } = useSharedQuery();
	const { data: privateSubnets } = usePrivateQuery(user?.tenancyId ?? '');
	const allSubnets = [...(sharedSubnets ?? []), ...(privateSubnets ?? [])];

	const subnetMap = new Map(allSubnets.map((s) => [s.id, s.cidr]));

	const [activeFilter, setActiveFilter] = useState<AllocationFilter | undefined>(undefined);

	const { useAllocationsQuery, useRelease } = useAllocations();
	const { data, isLoading, error } = useAllocationsQuery(activeFilter);
	const releaseMutation = useRelease();

	const [releaseTarget, setReleaseTarget] = useState<Allocation | null>(null);
	const [tagsTarget, setTagsTarget] = useState<Allocation | null>(null);

	const allocateModal = useModal((onClose) => {
		return <AllocateModal open onClose={onClose} />;
	});

	const bulkAllocateModal = useModal((onClose) => {
		return <BulkAllocateModal open onClose={onClose} />;
	});

	const releaseModal = useModal((onClose) => {
		function handleClose() {
			setReleaseTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Release Allocation"
				message={`Release IP ${releaseTarget?.ipAddress ?? ''}? This will make it available for re-allocation.`}
				confirmLabel="Release"
				onConfirm={() => {
					void handleRelease();
				}}
				onClose={handleClose}
				isLoading={releaseMutation.isPending}
			/>
		);
	});

	const tagsModal = useModal((onClose) => {
		function handleClose() {
			setTagsTarget(null);
			onClose();
		}

		return <TagsModal allocation={tagsTarget} onClose={handleClose} />;
	});

	const handleSearchChange = useCallback((term: string) => {
		setActiveFilter(parseTagFilter(term));
	}, []);

	const columns: ColumnDef<Allocation>[] = [
		{
			key: 'ipAddress',
			header: 'IP Address',
			render: (a) => <code>{a.ipAddress}</code>,
			sortValue: (a) => a.ipAddress,
		},
		{ key: 'description', header: 'Description', render: (a) => a.description, sortValue: (a) => a.description },
		{
			key: 'subnetId',
			header: 'Subnet',
			render: (a) => (
				<CopyableId label={subnetMap.get(a.subnetId) ?? `${a.subnetId.slice(0, 8)}…`} fullId={a.subnetId} />
			),
			sortValue: (a) => subnetMap.get(a.subnetId) ?? '',
		},
		{
			key: 'allocatedAt',
			header: 'Allocated',
			render: (a) => formatTs(a.allocatedAt),
			sortValue: (a) => new Date(a.allocatedAt).getTime(),
		},
		{
			key: 'bulkId',
			header: 'Bulk',
			render: (a) =>
				a.bulkId !== null ? (
					<Tag type="blue" size="sm">
						Bulk
					</Tag>
				) : null,
		},
	];

	const rowActions: RowAction<Allocation>[] = [
		{
			label: 'Manage Tags',
			onClick: (a) => {
				setTagsTarget(a);
				tagsModal.open();
			},
		},
		{
			label: 'Release',
			danger: true,
			onClick: (a) => {
				setReleaseTarget(a);
				releaseModal.open();
			},
		},
	];

	async function handleRelease() {
		if (releaseTarget === null) {
			return;
		}
		await releaseMutation.mutateAsync(releaseTarget.id);
		setReleaseTarget(null);
		releaseModal.close();
	}

	return (
		<>
			<ErrorBanner error={error} title="Failed to load allocations" />
			<IpamDataTable
				columns={columns}
				rows={data ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No allocations found."
				onSearchChange={handleSearchChange}
				searchable
				toolbarContent={
					<>
						<Button
							kind="tertiary"
							onClick={() => {
								bulkAllocateModal.open();
							}}
						>
							Bulk Allocate
						</Button>
						<Button
							kind="primary"
							onClick={() => {
								allocateModal.open();
							}}
						>
							Allocate IP
						</Button>
					</>
				}
			/>
			{allocateModal.modal}
			{bulkAllocateModal.modal}
			{tagsModal.modal}
			{releaseModal.modal}
		</>
	);
}
