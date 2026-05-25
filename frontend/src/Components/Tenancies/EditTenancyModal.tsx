import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTenancies } from '../../Hooks/useTenancies';
import { updateTenancySchema, type Tenancy, type UpdateTenancyRequest } from '../../Services/TenanciesService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface EditTenancyModalProps {
	tenancy: Tenancy | null;
	onClose: () => void;
}

export function EditTenancyModal({ tenancy, onClose }: EditTenancyModalProps) {
	const { useUpdate } = useTenancies();
	const mutation = useUpdate();
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<UpdateTenancyRequest>({
		resolver: zodResolver(updateTenancySchema),
		values: tenancy !== null ? { name: tenancy.name, description: tenancy.description } : undefined,
	});

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		if (tenancy === null) {
			return;
		}
		await mutation.mutateAsync({ id: tenancy.id, request: data });
		handleClose();
	});

	return (
		<Modal
			open={tenancy !== null}
			modalHeading="Edit Tenancy"
			primaryButtonText={mutation.isPending ? 'Saving…' : 'Save'}
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
					<ErrorBanner error={mutation.error} title="Failed to update tenancy" />
					<TextInput
						id="name"
						labelText="Tenancy name"
						invalid={!!errors.name}
						invalidText={errors.name?.message}
						{...register('name')}
					/>
					<TextInput
						id="description"
						labelText="Description"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
