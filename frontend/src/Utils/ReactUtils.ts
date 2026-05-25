import { useEffect, useState } from 'react';
import { breakpoints } from '@carbon/layout';

/**
 * Utility function to conditionally combine CSS class names. Accepts any number
 * of string arguments, as well as undefined or false values which are ignored.
 * Returns a single space-separated string of valid class names.
 *
 * Example usage:
 *   const buttonClass = classNames('btn', isActive && 'btn-active', theme);
 *   // If isActive is true and theme is 'dark', buttonClass will be "btn btn-active dark".
 */
export function classNames(...classes: (string | undefined | false | null)[]) {
	return classes.filter(Boolean).join(' ');
}

/**
 * Tracks whether the viewport is at or above Carbon's lg breakpoint — the
 * point at which the Shell side nav switches from a modal overlay to a
 * persistent rail. Updates reactively as the window is resized.
 */
export function useIsDesktop(): boolean {
	const query = `(min-width: ${breakpoints.lg.width})`;
	const [matches, setMatches] = useState(() => window.matchMedia(query).matches);

	useEffect(() => {
		const mql = window.matchMedia(query);
		const handler = (e: MediaQueryListEvent) => {
			setMatches(e.matches);
		};
		mql.addEventListener('change', handler);
		return () => {
			mql.removeEventListener('change', handler);
		};
	}, [query]);

	return matches;
}
