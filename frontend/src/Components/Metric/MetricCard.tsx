import { Tile } from '@carbon/react';
import styles from './MetricCard.module.scss';
import { classNames } from '../../Utils/ReactUtils';

interface MetricCardProps {
	/** Short label describing what the metric measures. */
	label: string;
	/** The metric value — typically a number or percentage string. */
	value: string | number;
	/** Optional unit suffix, e.g. "%" or "IPs". */
	unit?: string;
	/** Optional contextual colour */
	intent?: 'success' | 'warning' | 'error';
}

/**
 * A single-stat display card built on Carbon Tile. Used across all three
 * dashboard views to present key metrics in a consistent visual format.
 */
export function MetricCard({ label, value, unit, intent }: MetricCardProps) {
	return (
		<Tile className={styles.tile}>
			<p className={styles.label}>{label}</p>
			<p className={classNames(styles.value, intent)}>
				{value}
				{unit !== undefined && <span className={styles.unit}>{unit}</span>}
			</p>
		</Tile>
	);
}
