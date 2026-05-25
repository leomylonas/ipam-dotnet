import { Page } from './Page';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { AllocationList } from '../Components/Allocations/AllocationList';

export function AllocationsPage() {
	return (
		<Page>
			<PageHeader title="Allocations" description="View and manage IP address allocations." />
			<AllocationList />
		</Page>
	);
}
