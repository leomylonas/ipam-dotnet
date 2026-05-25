import { useState } from 'react';
import { Button } from '@carbon/react';
import { Add } from '@carbon/icons-react';
import { useModal } from '../../Hooks/useModal';
import { useSubnets } from '../../Hooks/useSubnets';
import { useTenancies } from '../../Hooks/useTenancies';
import { type Tenancy } from '../../Services/TenanciesService';
import { IpamDataTable, type ColumnDef, type RowAction } from '../DataTable/IpamDataTable';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { ConfirmModal } from '../Modal/ConfirmModal';
import { Section } from '../Section/Section';
import { GrantAccessModal } from './GrantAccessModal';

interface AccessSectionProps {
	subnetId: string;
}

const columns: ColumnDef<Tenancy>[] = [
	{ key: 'name', header: 'Tenancy', render: (t) => t.name, sortValue: (t) => t.name },
	{ key: 'description', header: 'Description', render: (t) => t.description, sortValue: (t) => t.description },
];

export function AccessSection({ subnetId }: AccessSectionProps) {
	const { useAccessQuery, useRevokeAccess } = useSubnets();
	const { useTenanciesQuery } = useTenancies();

	const { data: grants, isLoading, error } = useAccessQuery(subnetId);
	const { data: tenancies } = useTenanciesQuery();
	const revokeMutation = useRevokeAccess(subnetId);

	const grantedIds = new Set((grants ?? []).map((g) => g.id));
	const availableTenancies = (tenancies ?? []).filter((t) => !grantedIds.has(t.id));

	const [revokeTarget, setRevokeTarget] = useState<Tenancy | null>(null);
	const grantModal = useModal((onClose) => {
		return <GrantAccessModal open onClose={onClose} subnetId={subnetId} availableTenancies={availableTenancies} />;
	});
	const revokeModal = useModal((onClose) => {
		function handleClose() {
			setRevokeTarget(null);
			onClose();
		}

		return (
			<ConfirmModal
				open
				heading="Revoke Access"
				message={`Revoke access for "${revokeTarget?.name ?? ''}"? They will no longer be able to allocate IPs from this subnet.`}
				confirmLabel="Revoke"
				onConfirm={() => {
					void handleRevoke();
				}}
				onClose={handleClose}
				isLoading={revokeMutation.isPending}
			/>
		);
	});

	const rowActions: RowAction<Tenancy>[] = [
		{
			label: 'Revoke',
			danger: true,
			onClick: (t) => {
				setRevokeTarget(t);
				revokeModal.open();
			},
		},
	];

	async function handleRevoke() {
		if (revokeTarget === null) {
			return;
		}
		await revokeMutation.mutateAsync(revokeTarget.id);
		setRevokeTarget(null);
		revokeModal.close();
	}

	return (
		<Section title="Tenancy Access">
			<ErrorBanner error={error} title="Failed to load access grants" />
			<IpamDataTable
				columns={columns}
				rows={grants ?? []}
				isLoading={isLoading}
				rowActions={rowActions}
				emptyMessage="No tenancies have been granted access — this subnet is inaccessible."
				toolbarContent={
					<Button
						kind="primary"
						renderIcon={Add}
						disabled={availableTenancies.length === 0}
						onClick={() => {
							grantModal.open();
						}}
					>
						Grant Access
					</Button>
				}
			/>
			{grantModal.modal}
			{revokeModal.modal}
		</Section>
	);
}
