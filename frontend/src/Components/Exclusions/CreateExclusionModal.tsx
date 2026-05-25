import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useExclusions } from '../../Hooks/useExclusions';
import { createExclusionSchema, type CreateExclusionRequest } from '../../Services/ExclusionsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface CreateExclusionModalProps {
	open: boolean;
	onClose: () => void;
	subnetId: string;
}

export function CreateExclusionModal({ open, onClose, subnetId }: CreateExclusionModalProps) {
	const { useCreate } = useExclusions();
	const mutation = useCreate(subnetId);
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<CreateExclusionRequest>({
		resolver: zodResolver(createExclusionSchema),
		defaultValues: { start: '', end: '', description: '' },
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
			modalHeading="Add Exclusion Range"
			primaryButtonText={mutation.isPending ? 'Adding…' : 'Add'}
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
					<ErrorBanner error={mutation.error} title="Failed to add exclusion" />
					<TextInput
						id="start"
						labelText="Start IP"
						placeholder="e.g. 192.168.1.1"
						helperText="For a single IP exclusion, set start and end to the same address."
						invalid={!!errors.start}
						invalidText={errors.start?.message}
						{...register('start')}
					/>
					<TextInput
						id="end"
						labelText="End IP"
						placeholder="e.g. 192.168.1.10"
						invalid={!!errors.end}
						invalidText={errors.end?.message}
						{...register('end')}
					/>
					<TextInput
						id="description"
						labelText="Description"
						placeholder="e.g. Gateway IP"
						invalid={!!errors.description}
						invalidText={errors.description?.message}
						{...register('description')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
