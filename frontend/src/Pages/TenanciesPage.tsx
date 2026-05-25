import { Page } from './Page';
import { useModal } from '../Hooks/useModal';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { TenancyList } from '../Components/Tenancies/TenancyList';
import { CreateTenancyModal } from '../Components/Tenancies/CreateTenancyModal';

export function TenanciesPage() {
	const createModal = useModal((onClose) => {
		return <CreateTenancyModal open onClose={onClose} />;
	});

	return (
		<Page>
			<PageHeader
				title="Tenancies"
				description="Manage isolated tenancy environments."
				addLabel="Create Tenancy"
				onAdd={() => {
					createModal.open();
				}}
			/>
			<TenancyList />
			{createModal.modal}
		</Page>
	);
}
