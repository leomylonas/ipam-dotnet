import { Modal, Form, Stack, Select, SelectItem } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSubnets } from '../../Hooks/useSubnets';
import { grantSubnetAccessSchema, type GrantSubnetAccessRequest } from '../../Services/SubnetsService';
import type { Tenancy } from '../../Services/TenanciesService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface GrantAccessModalProps {
	open: boolean;
	onClose: () => void;
	subnetId: string;
	/** Tenancies not yet granted — pre-filtered by the caller. */
	availableTenancies: Tenancy[];
}

export function GrantAccessModal({ open, onClose, subnetId, availableTenancies }: GrantAccessModalProps) {
	const { useGrantAccess } = useSubnets();
	const mutation = useGrantAccess(subnetId);
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<GrantSubnetAccessRequest>({
		resolver: zodResolver(grantSubnetAccessSchema),
		defaultValues: { tenancyId: '' as unknown as string },
	});

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		await mutation.mutateAsync(data);
		handleClose();
	});

	return (
		<Modal
			open={open}
			modalHeading="Grant Tenancy Access"
			primaryButtonText={mutation.isPending ? 'Granting…' : 'Grant'}
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
					<ErrorBanner error={mutation.error} title="Failed to grant access" />
					<Select
						id="tenancyId"
						labelText="Tenancy"
						invalid={!!errors.tenancyId}
						invalidText={errors.tenancyId?.message}
						{...register('tenancyId')}
					>
						<SelectItem value="" text="Select a tenancy…" />
						{availableTenancies.map((t) => (
							<SelectItem key={t.id} value={t.id} text={t.name} />
						))}
					</Select>
				</Stack>
			</Form>
		</Modal>
	);
}
