import { Page } from './Page';
import { useModal } from '../Hooks/useModal';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { SharedSubnetList } from '../Components/SharedSubnets/SharedSubnetList';
import { CreateSharedSubnetModal } from '../Components/SharedSubnets/CreateSharedSubnetModal';

export function SharedSubnetsPage() {
	const createModal = useModal((onClose) => {
		return <CreateSharedSubnetModal open onClose={onClose} />;
	});

	return (
		<Page>
			<PageHeader
				title="Shared Subnets"
				description="Shared subnets are not accessible to any tenancy until explicitly granted."
				addLabel="Create Shared Subnet"
				onAdd={() => {
					createModal.open();
				}}
			/>
			<SharedSubnetList />
			{createModal.modal}
		</Page>
	);
}
