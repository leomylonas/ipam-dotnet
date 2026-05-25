import React from 'react';
import { Button } from '@carbon/react';
import { Add } from '@carbon/icons-react';
import styles from './PageHeader.module.scss';

interface PageHeaderProps {
	/** Main heading text for the page. */
	title: string;
	/** Optional secondary text beneath the title. */
	description?: React.ReactNode;
	/** Label for the primary action button. When omitted, no button is rendered. */
	addLabel?: string;
	/** Handler for the primary action button click. */
	onAdd?: () => void;
	/** Whether the add button is disabled (e.g. insufficient permissions). */
	addDisabled?: boolean;
}

/**
 * Reusable page header with a title, optional description, and an optional
 * primary "Add" action button. Keeps page components lean by extracting this
 * repeated layout pattern.
 */
export function PageHeader({ title, description, addLabel, onAdd, addDisabled = false }: PageHeaderProps) {
	return (
		<div className={styles.header}>
			<div className={styles.text}>
				<h1 className={styles.title}>{title}</h1>
				{description !== undefined && <p className={styles.description}>{description}</p>}
			</div>
			{onAdd !== undefined && addLabel !== undefined && (
				<Button kind="primary" renderIcon={Add} onClick={onAdd} disabled={addDisabled}>
					{addLabel}
				</Button>
			)}
		</div>
	);
}
