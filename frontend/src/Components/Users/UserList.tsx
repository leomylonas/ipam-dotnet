import { useState } from 'react';
import { useModal } from '../../Hooks/useModal';
import { Tag } from '@carbon/react';
import { useUsers } from '../../Hooks/useUsers';
import { useTenancies } from '../../Hooks/useTenancies';
import { useAuthStore } from '../../Stores/AuthStore';
import { Roles, roleLabel } from '../../Services/AuthService';
import { type User } from '../../Services/UsersService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { CopyableId } from '../CopyableId/CopyableId';
import { EditUserModal } from './EditUserModal';

export function UserList() {
	const { user: caller } = useAuthStore();
	const isGlobalAdmin = caller?.role === Roles.GlobalAdmin;

	const { useUsersQuery, useDelete } = useUsers();
	const { data, isLoading, error } = useUsersQuery();
	const deleteMutation = useDelete();

	const { useTenanciesQuery } = useTenancies();
	const { data: tenancies } = useTenanciesQuery();

	const tenancyNames = new Map((tenancies ?? []).map((t) => [t.id, t.name]));

	const columns: ColumnDef<User>[] = [
		{ key: 'username', header: 'Username', render: (u) => u.username, sortValue: (u) => u.username },
		{
			key: 'role',
			header: 'Role',
			render: (u) => (
				<Tag type={u.role === Roles.GlobalAdmin ? 'purple' : u.role === Roles.TenantAdmin ? 'blue' : 'teal'}>
					{roleLabel[u.role] ?? u.role}
				</Tag>
			),
			sortValue: (u) => u.role,
		},
		{
			key: 'tenancyId',
			header: 'Tenancy',
			render: (u) =>
				u.tenancyId !== null ? (
					<CopyableId
						label={tenancyNames.get(u.tenancyId) ?? `${u.tenancyId.slice(0, 8)}…`}
						fullId={u.tenancyId}
					/>
				) : (
					'—'
				),
			sortValue: (u) => tenancyNames.get(u.tenancyId ?? '') ?? '',
		},
	];

	const [editTarget, setEditTarget] = useState<User | null>(null);
	const [deleteTarget, setDeleteTarget] = useState<User | null>(null);
	const editModal = useModal((onClose) => {
		function handleClose() {
			setEditTarget(null);
			onClose();
		}

		return (
			<EditUserModal
				user={editTarget}
				onClose={handleClose}
				isGlobalAdmin={isGlobalAdmin}
				callerId={caller?.id}
			/>
		);
	});
	const deleteModal = useModal((onClose) => {
		function handleClose() {
			setDeleteTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Delete User"
				message={`Delete user "${deleteTarget?.username ?? ''}"? This action cannot be undone.`}
				onConfirm={() => {
					void handleDelete();
				}}
				onClose={handleClose}
				isLoading={deleteMutation.isPending}
			/>
		);
	});

	const rowActions: RowAction<User>[] = [
		{
			label: 'Edit',
			onClick: (u) => {
				setEditTarget(u);
				editModal.open();
			},
		},
		{
			label: 'Delete',
			danger: true,
			onClick: (u) => {
				setDeleteTarget(u);
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
			<ErrorBanner error={error} title="Failed to load users" />
			<IpamDataTable
				columns={columns}
				rows={data ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No users found."
			/>
			{editModal.modal}
			{deleteModal.modal}
		</>
	);
}
