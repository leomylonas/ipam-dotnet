import { Modal } from '@carbon/react';

interface ConfirmModalProps {
	/** Whether the modal is visible. */
	open: boolean;
	/** Called when the user confirms the destructive action. */
	onConfirm: () => void;
	/** Called when the user cancels or dismisses the modal. */
	onClose: () => void;
	/** Short heading for the modal, e.g. "Delete Tenancy". */
	heading: string;
	/** Descriptive message explaining what will be deleted and whether it's irreversible. */
	message: string;
	/** Whether the confirm action is in progress — disables the confirm button. */
	isLoading?: boolean;
	/** Label for the confirm button. Defaults to "Delete". */
	confirmLabel?: string;
}

/**
 * A Carbon Modal preconfigured as a danger confirmation dialog. Used for all
 * destructive operations across the app so the pattern is consistent.
 * The caller controls visibility and provides the confirm/cancel handlers.
 */
export function ConfirmModal({
	open,
	onConfirm,
	onClose,
	heading,
	message,
	isLoading = false,
	confirmLabel = 'Delete',
}: ConfirmModalProps) {
	const loadingLabel = `${confirmLabel.replace(/e$/, '')}ing…`;
	return (
		<Modal
			open={open}
			danger
			modalHeading={heading}
			primaryButtonText={isLoading ? loadingLabel : confirmLabel}
			primaryButtonDisabled={isLoading}
			secondaryButtonText="Cancel"
			onRequestSubmit={onConfirm}
			onRequestClose={onClose}
			onSecondarySubmit={onClose}
		>
			<p>{message}</p>
		</Modal>
	);
}
