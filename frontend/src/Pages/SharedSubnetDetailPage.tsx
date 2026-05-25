import { useNavigate, useParams } from '@tanstack/react-router';
import { useStats } from '../Hooks/useStats';
import { useSubnets } from '../Hooks/useSubnets';
import { Page } from './Page';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { SubnetMetrics } from '../Components/Subnets/SubnetMetrics';
import { AccessSection } from '../Components/SharedSubnets/AccessSection';
import { ExclusionSection } from '../Components/Exclusions/ExclusionSection';
import { AllocationSection } from '../Components/Allocations/AllocationSection';

export function SharedSubnetDetailPage() {
	const { subnetId } = useParams({ from: '/_app/shared-subnets/$subnetId' });
	const navigate = useNavigate();
	const { useSharedQuery } = useSubnets();
	const { data: sharedSubnets } = useSharedQuery();
	const subnet = (sharedSubnets ?? []).find((s) => s.id === subnetId);
	const { useSubnetStats } = useStats();
	const { data: stats } = useSubnetStats(subnetId);

	return (
		<Page
			back={{
				label: 'Back to Shared Subnets',
				onClick: () => {
					void navigate({ to: '/shared-subnets' });
				},
			}}
		>
			<PageHeader
				title={subnet !== undefined ? `${subnet.cidr} — ${subnet.name}` : `Subnet ${subnetId.slice(0, 8)}…`}
				description={subnet?.description ?? 'Utilisation, access control, exclusions, and active allocations.'}
			/>
			<SubnetMetrics stats={stats} />
			<AccessSection subnetId={subnetId} />
			<ExclusionSection subnetId={subnetId} />
			<AllocationSection subnetId={subnetId} />
		</Page>
	);
}
