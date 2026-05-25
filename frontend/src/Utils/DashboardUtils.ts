/** Formats an ISO-8601 UTC string into a compact locale-aware timestamp. */
export function formatTs(iso: string): string {
	return new Date(iso).toLocaleString();
}

/** Returns a MetricCard intent colour based on a utilisation percentage. */
export function utilisationIntent(pct: number): 'success' | 'warning' | 'error' {
	if (pct >= 90) return 'error';
	if (pct >= 80) return 'warning';
	return 'success';
}
