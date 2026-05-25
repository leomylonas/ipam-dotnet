import { Modal, Form, Stack, TextInput } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useExclusions } from '../../Hooks/useExclusions';
import { updateExclusionSchema, type Exclusion, type UpdateExclusionRequest } from '../../Services/ExclusionsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface EditExclusionModalProps {
	exclusion: Exclusion | null;
	onClose: () => void;
	subnetId: string;
}

export function EditExclusionModal({ exclusion, onClose, subnetId }: EditExclusionModalProps) {
	const { useUpdate } = useExclusions();
	const mutation = useUpdate(subnetId);
	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<UpdateExclusionRequest>({
		resolver: zodResolver(updateExclusionSchema),
		values: exclusion !== null ? { description: exclusion.description } : undefined,
	});

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		if (exclusion === null) {
			return;
		}
		await mutation.mutateAsync({ id: exclusion.id, request: data });
		handleClose();
	});

	return (
		<Modal
			open={exclusion !== null}
			modalHeading="Edit Exclusion"
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
					<ErrorBanner error={mutation.error} title="Failed to update exclusion" />
					{exclusion !== null && (
						<p>
							<strong>Range:</strong>{' '}
							<code>
								{exclusion.start === exclusion.end
									? exclusion.start
									: `${exclusion.start} – ${exclusion.end}`}
							</code>{' '}
							<span className="text-secondary">(immutable)</span>
						</p>
					)}
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
