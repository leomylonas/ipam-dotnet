import { Modal, Form, Stack, TextInput, Select, SelectItem } from '@carbon/react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAllocations } from '../../Hooks/useAllocations';
import { useSubnets } from '../../Hooks/useSubnets';
import { useTenancies } from '../../Hooks/useTenancies';
import { useAuthStore } from '../../Stores/AuthStore';
import { Roles } from '../../Services/AuthService';
import type { BulkAllocateRequest } from '../../Services/AllocationsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

const bulkFormSchema = z.object({
	tenancyId: z.string().optional(),
	subnetId: z.string().min(1, 'A subnet must be selected'),
	count: z.string().refine((v) => {
		const n = parseInt(v, 10);
		return !isNaN(n) && n >= 1 && n <= 256;
	}, 'Count must be an integer between 1 and 256'),
	description: z.string().min(1, 'Description is required'),
});

interface BulkAllocateModalProps {
	open: boolean;
	onClose: () => void;
}

export function BulkAllocateModal({ open, onClose }: BulkAllocateModalProps) {
	const { user } = useAuthStore();
	const isGlobalAdmin = user?.role === Roles.GlobalAdmin;

	const { useBulkAllocate } = useAllocations();
	const mutation = useBulkAllocate();

	const { useSharedQuery, usePrivateQuery } = useSubnets();
	const { useTenanciesQuery } = useTenancies();

	const { data: sharedSubnets } = useSharedQuery();
	const { data: tenancies } = useTenanciesQuery();

	const {
		register,
		handleSubmit,
		reset,
		control,
		formState: { errors, isSubmitting },
	} = useForm({
		resolver: zodResolver(bulkFormSchema),
		defaultValues: { tenancyId: '', subnetId: '', count: '2', description: '' },
	});

	const selectedTenancyId = useWatch({ control, name: 'tenancyId' }) ?? '';
	const { data: privateSubnets } = usePrivateQuery(isGlobalAdmin ? selectedTenancyId : (user?.tenancyId ?? ''));

	const subnetOptions = isGlobalAdmin
		? [
				...(privateSubnets ?? []).map((s) => ({ value: s.id, label: `${s.cidr} — ${s.name} (private)` })),
				...(sharedSubnets ?? []).map((s) => ({ value: s.id, label: `${s.cidr} — ${s.name} (shared)` })),
			]
		: [
				...(sharedSubnets ?? []).map((s) => ({ value: s.id, label: `${s.cidr} — ${s.name}` })),
				...(privateSubnets ?? []).map((s) => ({ value: s.id, label: `${s.cidr} — ${s.name}` })),
			];

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		const request: BulkAllocateRequest = {
			subnetId: data.subnetId,
			count: parseInt(data.count, 10),
			description: data.description,
		};
		await mutation.mutateAsync(request);
		handleClose();
	});

	return (
		<Modal
			open={open}
			modalHeading="Bulk Allocate IPs"
			primaryButtonText={mutation.isPending ? 'Allocating…' : 'Allocate'}
			primaryButtonDisabled={mutation.isPending || isSubmitting}
			secondaryButtonText="Cancel"
			onRequestSubmit={() => {
				void onSubmit();
			}}
			onRequestClose={handleClose}
			onSecondarySubmit={handleClose}
		>
			<Form>
				<Stack gap={5}>
					<ErrorBanner error={mutation.error} title="Failed to bulk allocate" />
					<p className="text-secondary">
						Requests N consecutive IP addresses from the subnet. Returns an error if no contiguous block of
						the requested size is available.
					</p>
					{isGlobalAdmin && (
						<Select
							id="bulk-tenancyId"
							labelText="Tenant"
							invalid={!!errors.tenancyId}
							invalidText={errors.tenancyId?.message}
							{...register('tenancyId')}
						>
							<SelectItem value="" text="Select a tenant" />
							{(tenancies ?? []).map((t) => (
								<SelectItem key={t.id} value={t.id} text={t.name} />
							))}
						</Select>
					)}
					<Select
						id="bulk-subnetId"
						labelText="Subnet"
						invalid={!!errors.subnetId}
						invalidText={errors.subnetId?.message}
						{...register('subnetId')}
					>
						<SelectItem value="" text="Select a subnet" />
						{subnetOptions.map((opt) => (
							<SelectItem key={opt.value} value={opt.value} text={opt.label} />
						))}
					</Select>
					<TextInput
						id="bulk-count"
						labelText="Count"
						type="number"
						placeholder="Number of consecutive IPs"
						invalid={!!errors.count}
						invalidText={errors.count?.message}
						{...register('count')}
					/>
					<TextInput
						id="bulk-description"
						labelText="Description"
						placeholder="Applied to all allocations in this bulk request"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
