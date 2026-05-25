import { useState } from 'react';
import { useModal } from '../../Hooks/useModal';
import { useTenancies } from '../../Hooks/useTenancies';
import { type Tenancy } from '../../Services/TenanciesService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { EditTenancyModal } from './EditTenancyModal';

const columns: ColumnDef<Tenancy>[] = [
	{ key: 'name', header: 'Name', render: (t) => t.name, sortValue: (t) => t.name },
	{ key: 'description', header: 'Description', render: (t) => t.description, sortValue: (t) => t.description },
	{
		key: 'createdAt',
		header: 'Created',
		render: (t) => new Date(t.createdAt).toLocaleDateString(),
		sortValue: (t) => new Date(t.createdAt).getTime(),
	},
];

export function TenancyList() {
	const { useTenanciesQuery, useDelete } = useTenancies();
	const { data, isLoading, error } = useTenanciesQuery();
	const deleteMutation = useDelete();

	const [editTarget, setEditTarget] = useState<Tenancy | null>(null);
	const [deleteTarget, setDeleteTarget] = useState<Tenancy | null>(null);
	const editModal = useModal((onClose) => {
		function handleClose() {
			setEditTarget(null);
			onClose();
		}

		return <EditTenancyModal tenancy={editTarget} onClose={handleClose} />;
	});
	const deleteModal = useModal((onClose) => {
		function handleClose() {
			setDeleteTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Delete Tenancy"
				message={`Delete "${deleteTarget?.name ?? ''}" and all its associated data? This action is irreversible.`}
				onConfirm={() => {
					void handleDelete();
				}}
				onClose={handleClose}
				isLoading={deleteMutation.isPending}
			/>
		);
	});

	const rowActions: RowAction<Tenancy>[] = [
		{
			label: 'Edit',
			onClick: (t) => {
				setEditTarget(t);
				editModal.open();
			},
		},
		{
			label: 'Delete',
			danger: true,
			onClick: (t) => {
				setDeleteTarget(t);
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
		<>
			<ErrorBanner error={error} title="Failed to load tenancies" />
			<IpamDataTable
				columns={columns}
				rows={data ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No tenancies found. Create one to get started."
			/>
			{editModal.modal}
			{deleteModal.modal}
		</>
	);
}
