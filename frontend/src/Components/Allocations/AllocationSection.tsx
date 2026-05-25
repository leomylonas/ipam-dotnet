import { useState } from 'react';
import { Tag } from '@carbon/react';
import { useAllocations } from '../../Hooks/useAllocations';
import { useModal } from '../../Hooks/useModal';
import { type Allocation } from '../../Services/AllocationsService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { Section } from '../Section/Section';

interface AllocationSectionProps {
	subnetId: string;
}

const columns: ColumnDef<Allocation>[] = [
	{
		key: 'ipAddress',
		header: 'IP Address',
		render: (a) => <code>{a.ipAddress}</code>,
		sortValue: (a) => a.ipAddress,
	},
	{ key: 'description', header: 'Description', render: (a) => a.description, sortValue: (a) => a.description },
	{
		key: 'allocatedAt',
		header: 'Allocated',
		render: (a) => new Date(a.allocatedAt).toLocaleString(),
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

export function AllocationSection({ subnetId }: AllocationSectionProps) {
	const { useAllocationsQuery, useRelease } = useAllocations();
	const { data: allocations, isLoading, error } = useAllocationsQuery();
	const releaseMutation = useRelease();

	const [releaseTarget, setReleaseTarget] = useState<Allocation | null>(null);
	const releaseModal = useModal((onClose) => {
		function handleClose() {
			setReleaseTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading={`Release IP ${releaseTarget?.ipAddress ?? ''}`}
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

	const subnetAllocations = (allocations ?? []).filter((a) => a.subnetId === subnetId);

	const rowActions: RowAction<Allocation>[] = [
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
		<Section title="Active Allocations">
			<ErrorBanner error={error} title="Failed to load allocations" />
			<IpamDataTable
				columns={columns}
				rows={subnetAllocations}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No active allocations in this subnet."
			/>
			{releaseModal.modal}
		</Section>
	);
}
