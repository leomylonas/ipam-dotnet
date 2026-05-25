import { useState } from 'react';
import { Modal, Form, Stack, TextInput, Select, SelectItem } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSubnets } from '../../Hooks/useSubnets';
import { useTenancies } from '../../Hooks/useTenancies';
import { createSubnetSchema, type CreateSubnetRequest } from '../../Services/SubnetsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import { Rfc1918Term } from '../Misc/Rfc1918Term';

interface CreateSubnetModalProps {
	open: boolean;
	onClose: () => void;
	/** Pre-selected tenancy from the page filter (may be empty for GlobalAdmin). */
	initialTenancyId: string;
	isGlobalAdmin: boolean;
}

export function CreateSubnetModal({ open, onClose, initialTenancyId, isGlobalAdmin }: CreateSubnetModalProps) {
	const { useTenanciesQuery } = useTenancies();
	const { data: tenancies } = useTenanciesQuery();

	// GlobalAdmin selects a tenancy inside the modal; TenantAdmin always uses
	// their own. Track it as local state so the mutation hook stays stable.
	const [modalTenancyId, setModalTenancyId] = useState(initialTenancyId);

	const { useCreatePrivate } = useSubnets();
	const mutation = useCreatePrivate(modalTenancyId);
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<CreateSubnetRequest>({
		resolver: zodResolver(createSubnetSchema),
		defaultValues: { cidr: '', name: '', description: '' },
	});

	function handleClose() {
		reset();
		mutation.reset();
		setModalTenancyId(initialTenancyId);
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		await mutation.mutateAsync(data);
		handleClose();
	});

	return (
		<Modal
			open={open}
			modalHeading="Create Private Subnet"
			primaryButtonText={mutation.isPending ? 'Creating…' : 'Create'}
			primaryButtonDisabled={mutation.isPending || isSubmitting || !modalTenancyId}
			secondaryButtonText="Cancel"
			onRequestSubmit={() => {
				void onSubmit();
			}}
			onRequestClose={handleClose}
			onSecondarySubmit={handleClose}
		>
			<Form>
				<Stack gap={5}>
					<ErrorBanner error={mutation.error} title="Failed to create subnet" />
					{isGlobalAdmin && (
						<Select
							id="modal-tenancyId"
							labelText="Tenancy"
							value={modalTenancyId}
							onChange={(e) => {
								setModalTenancyId(e.target.value);
							}}
						>
							<SelectItem value="" text="Select a tenancy…" />
							{(tenancies ?? []).map((t) => (
								<SelectItem key={t.id} value={t.id} text={t.name} />
							))}
						</Select>
					)}
					<TextInput
						id="cidr"
						labelText="CIDR"
						placeholder="e.g. 192.168.1.0/24"
						helperText={
							<>
								Must be <Rfc1918Term /> and non-overlapping within this tenancy.
							</>
						}
						invalid={!!errors.cidr}
						invalidText={errors.cidr?.message}
						{...register('cidr')}
					/>
					<TextInput
						id="name"
						labelText="Name"
						placeholder="Human-readable label"
						invalid={!!errors.name}
						invalidText={errors.name?.message}
						{...register('name')}
					/>
					<TextInput
						id="description"
						labelText="Description"
						placeholder="Purpose of this subnet"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
