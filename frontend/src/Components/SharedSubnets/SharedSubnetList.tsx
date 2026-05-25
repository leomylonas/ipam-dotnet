import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useModal } from '../../Hooks/useModal';
import { useSubnets } from '../../Hooks/useSubnets';
import { type Subnet } from '../../Services/SubnetsService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { EditSharedSubnetModal } from './EditSharedSubnetModal';

const columns: ColumnDef<Subnet>[] = [
	{ key: 'cidr', header: 'CIDR', render: (s) => <code>{s.cidr}</code>, sortValue: (s) => s.cidr },
	{ key: 'name', header: 'Name', render: (s) => s.name, sortValue: (s) => s.name },
	{ key: 'description', header: 'Description', render: (s) => s.description, sortValue: (s) => s.description },
	{
		key: 'createdAt',
		header: 'Created',
		render: (s) => new Date(s.createdAt).toLocaleDateString(),
		sortValue: (s) => new Date(s.createdAt).getTime(),
	},
];

export function SharedSubnetList() {
	const navigate = useNavigate();
	const { useSharedQuery, useDeleteShared } = useSubnets();
	const { data, isLoading, error } = useSharedQuery();
	const deleteMutation = useDeleteShared();

	const [editTarget, setEditTarget] = useState<Subnet | null>(null);
	const [deleteTarget, setDeleteTarget] = useState<Subnet | null>(null);
	const editModal = useModal((onClose) => {
		function handleClose() {
			setEditTarget(null);
			onClose();
		}

		return <EditSharedSubnetModal subnet={editTarget} onClose={handleClose} />;
	});
	const deleteModal = useModal((onClose) => {
		function handleClose() {
			setDeleteTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Delete Shared Subnet"
				message={`Delete subnet "${deleteTarget?.cidr ?? ''}" (${deleteTarget?.name ?? ''})? All allocations and exclusions will be removed.`}
				onConfirm={() => {
					void handleDelete();
				}}
				onClose={handleClose}
				isLoading={deleteMutation.isPending}
			/>
		);
	});

	const rowActions: RowAction<Subnet>[] = [
		{
			label: 'Edit',
			onClick: (s) => {
				setEditTarget(s);
				editModal.open();
			},
		},
		{
			label: 'Delete',
			danger: true,
			onClick: (s) => {
				setDeleteTarget(s);
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
			<ErrorBanner error={error} title="Failed to load shared subnets" />
			<IpamDataTable
				columns={columns}
				rows={data ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				onRowClick={(s) => {
					void navigate({ to: '/shared-subnets/$subnetId', params: { subnetId: s.id } });
				}}
				emptyMessage="No shared subnets found."
			/>
			{editModal.modal}
			{deleteModal.modal}
		</>
	);
}
