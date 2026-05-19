import { useEffect, useRef, useState } from 'react';
import { HeaderGlobalAction, Popover, PopoverContent } from '@carbon/react';
import { Asleep, Checkmark, Light, Screen } from '@carbon/icons-react';
import { useTheme, type ThemeMode } from '../ThemeProvider';
import styles from './ThemeSwitcher.module.scss';

const themeOptions: { mode: ThemeMode; label: string; Icon: React.ComponentType }[] = [
	{ mode: 'light', label: 'Light', Icon: Light },
	{ mode: 'dark', label: 'Dark', Icon: Asleep },
	{ mode: 'system', label: 'System', Icon: Screen },
];

/** Header action button that opens a popover for switching between light, dark, and system themes. */
export function ThemeSwitcher() {
	const { mode, setMode } = useTheme();
	const [open, setOpen] = useState(false);
	const popoverRef = useRef<HTMLSpanElement>(null);

	// Close when the user clicks outside the popover.
	useEffect(() => {
		if (!open) {
			return;
		}
		const close = (e: MouseEvent) => {
			if (popoverRef.current != null && !popoverRef.current.contains(e.target as Node)) {
				setOpen(false);
			}
		};
		document.addEventListener('mousedown', close);
		return () => {
			document.removeEventListener('mousedown', close);
		};
	}, [open]);

	const CurrentIcon = themeOptions.find((o) => o.mode === mode)?.Icon ?? Screen;

	return (
		<Popover ref={popoverRef} className={styles.popover} open={open} align="bottom-right" dropShadow>
			<HeaderGlobalAction
				aria-label="Theme"
				data-testid="theme-toggle"
				isActive={open}
				onClick={() => {
					setOpen((o) => !o);
				}}
			>
				<CurrentIcon />
			</HeaderGlobalAction>
			<PopoverContent>
				<ul className={styles.menu}>
					{themeOptions.map(({ mode: m, label }) => (
						<li key={m}>
							<button
								className={styles.menuItem}
								onClick={() => {
									setMode(m);
									setOpen(false);
								}}
							>
								{label}
								{mode === m && <Checkmark />}
							</button>
						</li>
					))}
				</ul>
			</PopoverContent>
		</Popover>
	);
}
