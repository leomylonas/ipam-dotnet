import type { ReactNode } from 'react';
import styles from './MetricGrid.module.scss';

export function MetricGrid({ children }: { children: ReactNode }) {
	return <div className={styles.grid}>{children}</div>;
}
