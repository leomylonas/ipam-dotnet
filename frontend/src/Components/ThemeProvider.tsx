import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Theme } from '@carbon/react';

// ── Types ─────────────────────────────────────────────────────────────────────

/** The three theme modes the user can choose. */
export type ThemeMode = 'light' | 'dark' | 'system';

/** The resolved Carbon theme token actually applied to the DOM. */
type CarbonTheme = 'white' | 'g90';

interface ThemeContextValue {
	mode: ThemeMode;
	/** The Carbon theme token currently in effect (after resolving 'system'). */
	resolvedTheme: CarbonTheme;
	setMode: (mode: ThemeMode) => void;
}

// ── Context ───────────────────────────────────────────────────────────────────

const ThemeContext = createContext<ThemeContextValue>({
	mode: 'system',
	resolvedTheme: 'white',
	setMode: () => undefined,
});

/** Hook to read the current mode and resolved theme from any component. */
export function useTheme(): ThemeContextValue {
	return useContext(ThemeContext);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

const STORAGE_KEY = 'ipam-theme';

function getSystemTheme(): CarbonTheme {
	return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'g90' : 'white';
}

function resolveTheme(mode: ThemeMode): CarbonTheme {
	if (mode === 'light') return 'white';
	if (mode === 'dark') return 'g90';
	return getSystemTheme();
}

function readStoredMode(): ThemeMode {
	const stored = localStorage.getItem(STORAGE_KEY);
	if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
	return 'system';
}

// ── Provider ──────────────────────────────────────────────────────────────────

/**
 * Wraps the application in Carbon's Theme component and exposes the current
 * mode (light / dark / system) plus a setter via context.
 *
 * Behaviour:
 * - On mount, reads the stored mode from localStorage (defaults to 'system').
 * - 'system' follows prefers-color-scheme live; explicit modes ignore OS changes.
 * - Changing mode persists the choice to localStorage immediately.
 */
export function ThemeProvider({ children }: { children: React.ReactNode }) {
	const [mode, setModeState] = useState<ThemeMode>(readStoredMode);
	const [resolvedTheme, setResolvedTheme] = useState<CarbonTheme>(() => resolveTheme(readStoredMode()));

	const setMode = useCallback((next: ThemeMode) => {
		localStorage.setItem(STORAGE_KEY, next);
		setModeState(next);
		setResolvedTheme(resolveTheme(next));
	}, []);

	// When mode is 'system', keep resolvedTheme in sync with OS preference changes.
	useEffect(() => {
		const mq = window.matchMedia('(prefers-color-scheme: dark)');
		const handleChange = () => {
			if (readStoredMode() === 'system') {
				setResolvedTheme(getSystemTheme());
			}
		};
		mq.addEventListener('change', handleChange);
		return () => {
			mq.removeEventListener('change', handleChange);
		};
	}, []);

	return (
		<ThemeContext.Provider value={{ mode, resolvedTheme, setMode }}>
			<Theme theme={resolvedTheme}>{children}</Theme>
		</ThemeContext.Provider>
	);
}
