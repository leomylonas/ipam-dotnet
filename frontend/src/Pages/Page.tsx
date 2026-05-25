import type { ReactNode } from 'react';
import { Button } from '@carbon/react';
import { ArrowLeft } from '@carbon/icons-react';
import styles from './Page.module.scss';

interface PageProps {
	children: ReactNode;
	back?: {
		label: string;
		onClick: () => void;
	};
}

export function Page({ children, back }: PageProps) {
	return (
		<div className={styles.page}>
			{back !== undefined && (
				<Button
					kind="ghost"
					renderIcon={ArrowLeft}
					size="sm"
					className={styles.backButton}
					onClick={back.onClick}
				>
					{back.label}
				</Button>
			)}
			{children}
		</div>
	);
}
