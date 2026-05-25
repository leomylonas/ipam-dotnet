import type { ReactNode } from 'react';
import styles from './Section.module.scss';

interface SectionProps {
	title: string;
	children: ReactNode;
}

export function Section({ title, children }: SectionProps) {
	return (
		<section className={styles.section}>
			<h2 className={styles.title}>{title}</h2>
			{children}
		</section>
	);
}
