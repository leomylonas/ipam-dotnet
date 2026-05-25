import { Page } from './Page';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { AuditList } from '../Components/Audit/AuditList';

export function AuditPage() {
	return (
		<Page>
			<PageHeader
				title="Audit Log"
				description="All actions are recorded automatically. Entries are read-only and newest-first."
			/>
			<AuditList />
		</Page>
	);
}
