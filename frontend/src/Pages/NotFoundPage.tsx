import { Heading, Stack } from '@carbon/react';
import { Link } from '@tanstack/react-router';
import styles from './NotFoundPage.module.scss';

/**
 * 404 Not Found page — rendered by the root route's notFoundComponent for any
 * path that does not match a registered route. Also serves as the placeholder
 * for feature pages not yet implemented (Phase 3).
 */
export function NotFoundPage() {
	return (
		// Stack provides the flex-column layout and gap; the CSS module adds
		// centering and min-height which have no Carbon component equivalent.
		<Stack gap={4} className={styles.page}>
			<Heading>404</Heading>
			<p>This page does not exist or has not been implemented yet.</p>
			<Link to="/" className="cds--btn cds--btn--primary">
				Go to Dashboard
			</Link>
		</Stack>
	);
}
