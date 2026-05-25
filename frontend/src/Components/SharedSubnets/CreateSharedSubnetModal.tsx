import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSubnets } from '../../Hooks/useSubnets';
import { createSubnetSchema, type CreateSubnetRequest } from '../../Services/SubnetsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface CreateSharedSubnetModalProps {
	open: boolean;
	onClose: () => void;
}

export function CreateSharedSubnetModal({ open, onClose }: CreateSharedSubnetModalProps) {
	const { useCreateShared } = useSubnets();
	const mutation = useCreateShared();
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
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		await mutation.mutateAsync(data);
		handleClose();
	});

	return (
		<Modal
			open={open}
			modalHeading="Create Shared Subnet"
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
					<ErrorBanner error={mutation.error} title="Failed to create subnet" />
					<TextInput
						id="cidr"
						labelText="CIDR"
						placeholder="e.g. 10.0.0.0/24"
						helperText="Must not overlap with existing shared subnets."
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
