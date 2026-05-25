import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTenancies } from '../../Hooks/useTenancies';
import { createTenancySchema, type CreateTenancyRequest } from '../../Services/TenanciesService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface CreateTenancyModalProps {
	open: boolean;
	onClose: () => void;
}

export function CreateTenancyModal({ open, onClose }: CreateTenancyModalProps) {
	const { useCreate } = useTenancies();
	const mutation = useCreate();
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<CreateTenancyRequest>({
		resolver: zodResolver(createTenancySchema),
		defaultValues: { name: '', description: '', adminUsername: '', adminPassword: '' },
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
			modalHeading="Create Tenancy"
			primaryButtonText={mutation.isPending ? 'Creating…' : 'Create'}
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
					<ErrorBanner error={mutation.error} title="Failed to create tenancy" />
					<TextInput
						id="name"
						labelText="Tenancy name"
						placeholder="e.g. Production"
						invalid={!!errors.name}
						invalidText={errors.name?.message}
						{...register('name')}
					/>
					<TextInput
						id="description"
						labelText="Description"
						placeholder="Purpose of this tenancy"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
					<TextInput
						id="adminUsername"
						labelText="Admin username"
						placeholder="Initial TenantAdmin username"
						invalid={!!errors.adminUsername}
						invalidText={errors.adminUsername?.message}
						{...register('adminUsername')}
					/>
					<TextInput
						id="adminPassword"
						labelText="Admin password"
						type="password"
						placeholder="Initial TenantAdmin password"
						invalid={!!errors.adminPassword}
						invalidText={errors.adminPassword?.message}
						{...register('adminPassword')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
