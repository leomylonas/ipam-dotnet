import { Modal, Form, Stack, TextInput, Select, SelectItem } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAllocations } from '../../Hooks/useAllocations';
import { useSubnets } from '../../Hooks/useSubnets';
import { useTenancies } from '../../Hooks/useTenancies';
import { useAuthStore } from '../../Stores/AuthStore';
import { Roles } from '../../Services/AuthService';
import { allocateSchema, type AllocateRequest } from '../../Services/AllocationsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { z } from 'zod';
import { useWatch } from 'react-hook-form';

const allocateFormSchema = allocateSchema.extend({
	tenancyId: z.string().optional(),
});
type AllocateFormValues = z.infer<typeof allocateFormSchema>;

interface AllocateModalProps {
	open: boolean;
	onClose: () => void;
}

export function AllocateModal({ open, onClose }: AllocateModalProps) {
	const { user } = useAuthStore();
	const isGlobalAdmin = user?.role === Roles.GlobalAdmin;

	const { useAllocate } = useAllocations();
	const mutation = useAllocate();

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
	} = useForm<AllocateFormValues>({
		resolver: zodResolver(allocateFormSchema),
		defaultValues: { tenancyId: '', subnetId: '' as unknown as string, description: '' },
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
		const request: AllocateRequest = { subnetId: data.subnetId, description: data.description };
		await mutation.mutateAsync(request);
		handleClose();
	});

	return (
		<Modal
			open={open}
			modalHeading="Allocate IP"
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
					<ErrorBanner error={mutation.error} title="Failed to allocate IP" />
					{isGlobalAdmin && (
						<Select
							id="tenancyId"
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
						id="subnetId"
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
						id="description"
						labelText="Description"
						placeholder="What will this IP be used for?"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
