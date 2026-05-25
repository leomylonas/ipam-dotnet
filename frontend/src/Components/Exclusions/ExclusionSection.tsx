import { useState } from 'react';
import { Button } from '@carbon/react';
import { useModal } from '../../Hooks/useModal';
import { useExclusions } from '../../Hooks/useExclusions';
import { type Exclusion } from '../../Services/ExclusionsService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { Section } from '../Section/Section';
import { CreateExclusionModal } from './CreateExclusionModal';
import { EditExclusionModal } from './EditExclusionModal';

interface ExclusionSectionProps {
	subnetId: string;
}

const columns: ColumnDef<Exclusion>[] = [
	{
		key: 'range',
		header: 'Range',
		render: (e) =>
			e.start === e.end ? (
				<code>{e.start}</code>
			) : (
				<code>
					{e.start} – {e.end}
				</code>
			),
		sortValue: (e) => e.start,
	},
	{ key: 'description', header: 'Description', render: (e) => e.description, sortValue: (e) => e.description },
];

export function ExclusionSection({ subnetId }: ExclusionSectionProps) {
	const { useExclusionsQuery, useDelete } = useExclusions();
	const { data: exclusions, isLoading, error } = useExclusionsQuery(subnetId);
	const deleteMutation = useDelete(subnetId);

	const [editTarget, setEditTarget] = useState<Exclusion | null>(null);
	const [deleteTarget, setDeleteTarget] = useState<Exclusion | null>(null);
	const createModal = useModal((onClose) => {
		return <CreateExclusionModal open onClose={onClose} subnetId={subnetId} />;
	});
	const editModal = useModal((onClose) => {
		function handleClose() {
			setEditTarget(null);
			onClose();
		}

		return <EditExclusionModal exclusion={editTarget} onClose={handleClose} subnetId={subnetId} />;
	});
	const deleteModal = useModal((onClose) => {
		function handleClose() {
			setDeleteTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Delete Exclusion"
				message={
					deleteTarget !== null
						? `Remove exclusion ${deleteTarget.start === deleteTarget.end ? deleteTarget.start : `${deleteTarget.start} – ${deleteTarget.end}`}?`
						: 'Remove this exclusion?'
				}
				onConfirm={() => {
					void handleDelete();
				}}
				onClose={handleClose}
				isLoading={deleteMutation.isPending}
			/>
		);
	});

	const rowActions: RowAction<Exclusion>[] = [
		{
			label: 'Edit',
			onClick: (e) => {
				setEditTarget(e);
				editModal.open();
			},
		},
		{
			label: 'Delete',
			danger: true,
			onClick: (e) => {
				setDeleteTarget(e);
				deleteModal.open();
			},
		},
	];

	async function handleDelete() {
		if (deleteTarget === null) {
			return;
		}
		await deleteMutation.mutateAsync(deleteTarget.id);
		setDeleteTarget(null);
		deleteModal.close();
	}

	return (
		<Section title="Exclusion Ranges">
			<ErrorBanner error={error} title="Failed to load exclusions" />
			<IpamDataTable
				columns={columns}
				rows={exclusions ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No exclusion ranges defined."
				toolbarContent={
					<Button
						kind="primary"
						onClick={() => {
							createModal.open();
						}}
					>
						Add Exclusion
					</Button>
				}
			/>
			{createModal.modal}
			{editModal.modal}
			{deleteModal.modal}
		</Section>
	);
}
