import { useState } from 'react';
import { Modal, Stack, Tag, TextInput, Button } from '@carbon/react';
import { useTags } from '../../Hooks/useTags';
import type { Allocation } from '../../Services/AllocationsService';
import type { TagsMap } from '../../Services/TagsService';
import { ErrorBanner } from '../Feedback/ErrorBanner';
import styles from './TagsModal.module.scss';

interface TagsModalProps {
	allocation: Allocation | null;
	onClose: () => void;
}

export function TagsModal({ allocation, onClose }: TagsModalProps) {
	const { useTagsQuery, useReplace } = useTags();
	const { data: tags, isLoading } = useTagsQuery(allocation?.id ?? '');
	const replaceMutation = useReplace(allocation?.id ?? '');

	const [entries, setEntries] = useState<{ key: string; value: string }[]>([]);
	const [newKey, setNewKey] = useState('');
	const [newValue, setNewValue] = useState('');

	// Sync local state from server whenever the allocation changes.
	if (tags !== undefined && entries.length === 0 && tags.length > 0) {
		setEntries(tags.map((t) => ({ key: t.key, value: t.value })));
	}

	function addEntry() {
		if (newKey.trim() === '' || newValue.trim() === '') {
			return;
		}
		setEntries((prev) => [...prev, { key: newKey.trim(), value: newValue.trim() }]);
		setNewKey('');
		setNewValue('');
	}

	function removeEntry(key: string) {
		setEntries((prev) => prev.filter((e) => e.key !== key));
	}

	async function handleSave() {
		if (allocation === null) {
			return;
		}
		const tagsMap: TagsMap = Object.fromEntries(entries.map((e) => [e.key, e.value]));
		await replaceMutation.mutateAsync(tagsMap);
		onClose();
	}

	function handleClose() {
		setEntries([]);
		setNewKey('');
		setNewValue('');
		replaceMutation.reset();
		onClose();
	}

	return (
		<Modal
			open={allocation !== null}
			modalHeading={`Tags — ${allocation?.ipAddress ?? ''}`}
			primaryButtonText={replaceMutation.isPending ? 'Saving…' : 'Save Tags'}
			primaryButtonDisabled={replaceMutation.isPending || isLoading}
			secondaryButtonText="Cancel"
			onRequestSubmit={() => {
				void handleSave();
			}}
			onRequestClose={handleClose}
			onSecondarySubmit={handleClose}
			size="md"
		>
			<Stack gap={4}>
				<ErrorBanner error={replaceMutation.error} title="Failed to save tags" />
				{entries.map((entry) => (
					<div key={entry.key} className={styles.tagRow}>
						<Tag type="blue">
							{entry.key}: {entry.value}
						</Tag>
						<Button
							kind="ghost"
							size="sm"
							onClick={() => {
								removeEntry(entry.key);
							}}
						>
							Remove
						</Button>
					</div>
				))}
				{entries.length === 0 && !isLoading && <p className="text-secondary">No tags set. Add one below.</p>}
				<div className={styles.addTagRow}>
					<TextInput
						id="new-tag-key"
						labelText="Key"
						value={newKey}
						onChange={(e) => {
							setNewKey(e.target.value);
						}}
						placeholder="e.g. env"
						size="sm"
					/>
					<TextInput
						id="new-tag-value"
						labelText="Value"
						value={newValue}
						onChange={(e) => {
							setNewValue(e.target.value);
						}}
						placeholder="e.g. production"
						size="sm"
					/>
					<Button kind="tertiary" size="sm" onClick={addEntry}>
						Add
					</Button>
				</div>
			</Stack>
		</Modal>
	);
}
