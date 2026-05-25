import { useState } from 'react';
import { Select, SelectItem } from '@carbon/react';
import { useModal } from '../Hooks/useModal';
import { useAuthStore } from '../Stores/AuthStore';
import { Roles } from '../Services/AuthService';
import { useTenancies } from '../Hooks/useTenancies';
import { Page } from './Page';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { SubnetList } from '../Components/Subnets/SubnetList';
import { CreateSubnetModal } from '../Components/Subnets/CreateSubnetModal';
import { Rfc1918Term } from '../Components/Misc/Rfc1918Term';

export function SubnetsPage() {
	const { user } = useAuthStore();
	const isGlobalAdmin = user?.role === Roles.GlobalAdmin;
	const { useTenanciesQuery } = useTenancies();
	const { data: tenancies } = useTenanciesQuery();
	const [selectedTenancyId, setSelectedTenancyId] = useState('');
	const tenancyId = isGlobalAdmin ? selectedTenancyId : (user?.tenancyId ?? '');
	const createModal = useModal((onClose) => {
		return <CreateSubnetModal open onClose={onClose} initialTenancyId={tenancyId} isGlobalAdmin={isGlobalAdmin} />;
	});

	return (
		<Page>
			<PageHeader
				title="Subnets"
				description={
					<>
						Private subnets must be <Rfc1918Term /> and non-overlapping within their tenancy.
					</>
				}
				addLabel="Create Subnet"
				onAdd={() => {
					createModal.open();
				}}
				addDisabled={isGlobalAdmin ? false : !tenancyId}
			/>
			{isGlobalAdmin && (
				<Select
					id="tenancy-filter"
					labelText="Tenancy"
					value={selectedTenancyId}
					onChange={(e) => {
						setSelectedTenancyId(e.target.value);
					}}
				>
					<SelectItem value="" text="Select a tenancy…" />
					{(tenancies ?? []).map((t) => (
						<SelectItem key={t.id} value={t.id} text={t.name} />
					))}
				</Select>
			)}
			<SubnetList tenancyId={tenancyId} />
			{createModal.modal}
		</Page>
	);
}
