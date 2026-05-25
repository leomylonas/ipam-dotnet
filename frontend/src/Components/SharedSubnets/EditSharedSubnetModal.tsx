import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSubnets } from '../../Hooks/useSubnets';
import { updateSubnetSchema, type Subnet, type UpdateSubnetRequest } from '../../Services/SubnetsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface EditSharedSubnetModalProps {
	subnet: Subnet | null;
	onClose: () => void;
}

export function EditSharedSubnetModal({ subnet, onClose }: EditSharedSubnetModalProps) {
	const { useUpdateShared } = useSubnets();
	const mutation = useUpdateShared();
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<UpdateSubnetRequest>({
		resolver: zodResolver(updateSubnetSchema),
		values: subnet !== null ? { name: subnet.name, description: subnet.description } : undefined,
	});

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		if (subnet === null) {
			return;
		}
		await mutation.mutateAsync({ id: subnet.id, request: data });
		handleClose();
	});

	return (
		<Modal
			open={subnet !== null}
			modalHeading="Edit Shared Subnet"
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
					<ErrorBanner error={mutation.error} title="Failed to update subnet" />
					{subnet !== null && (
						<p>
							<strong>CIDR:</strong> <code>{subnet.cidr}</code>{' '}
							<span className="text-secondary">(immutable)</span>
						</p>
					)}
					<TextInput
						id="name"
						labelText="Name"
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
