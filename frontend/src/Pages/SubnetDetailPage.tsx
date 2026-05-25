import { useNavigate, useParams } from '@tanstack/react-router';
import { useStats } from '../Hooks/useStats';
import { useSubnets } from '../Hooks/useSubnets';
import { useAuthStore } from '../Stores/AuthStore';
import { Page } from './Page';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { SubnetMetrics } from '../Components/Subnets/SubnetMetrics';
import { ExclusionSection } from '../Components/Exclusions/ExclusionSection';
import { AllocationSection } from '../Components/Allocations/AllocationSection';

export function SubnetDetailPage() {
	const { subnetId } = useParams({ from: '/_app/subnets/$subnetId' });
	const navigate = useNavigate();
	const { user } = useAuthStore();
	const { useSharedQuery, usePrivateQuery } = useSubnets();
	const { data: sharedSubnets } = useSharedQuery();
	const { data: privateSubnets } = usePrivateQuery(user?.tenancyId ?? '');
	const subnet = [...(sharedSubnets ?? []), ...(privateSubnets ?? [])].find((s) => s.id === subnetId);
	const { useSubnetStats } = useStats();
	const { data: stats } = useSubnetStats(subnetId);

	return (
		<Page
			back={{
				label: 'Back to Subnets',
				onClick: () => {
					void navigate({ to: '/subnets' });
				},
			}}
		>
			<PageHeader
				title={subnet !== undefined ? `${subnet.cidr} — ${subnet.name}` : `Subnet ${subnetId.slice(0, 8)}…`}
				description={subnet?.description ?? 'Utilisation, exclusions, and active allocations.'}
			/>
			<SubnetMetrics stats={stats} />
			<ExclusionSection subnetId={subnetId} />
			<AllocationSection subnetId={subnetId} />
		</Page>
	);
}
