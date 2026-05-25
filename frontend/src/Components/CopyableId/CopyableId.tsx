import { useState } from 'react';
import { Button, Tooltip } from '@carbon/react';
import { Copy, CopyLink } from '@carbon/icons-react';
import styles from './CopyableId.module.scss';

interface CopyableIdProps {
	/** Short human-readable label (e.g. username, CIDR, or truncated ID). */
	label: string;
	/** The full ID string shown in the tooltip and copied to clipboard on click. */
	fullId: string;
}

/**
 * Renders a label with an inline copy button. Hovering reveals the full ID in
 * a tooltip; clicking the button writes it to the clipboard and briefly swaps
 * the icon to confirm the action.
 */
export function CopyableId({ label, fullId }: CopyableIdProps) {
	const [copied, setCopied] = useState(false);

	function handleCopy(e: React.MouseEvent) {
		e.stopPropagation();
		void navigator.clipboard.writeText(fullId).then(() => {
			setCopied(true);
			setTimeout(() => {
				setCopied(false);
			}, 1500);
		});
	}

	return (
		<span className={styles.cell}>
			<span>{label}</span>
			<Tooltip label={<code className={styles.idFull}>{fullId}</code>} align="top">
				<Button
					kind="ghost"
					size="sm"
					className={styles.copyBtn}
					renderIcon={copied ? CopyLink : Copy}
					iconDescription="Copy ID"
					hasIconOnly
					onClick={handleCopy}
				/>
			</Tooltip>
		</span>
	);
}
