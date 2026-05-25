import { describe, it, expect } from 'vitest';
import { formatTs, utilisationIntent } from './DashboardUtils';

describe('formatTs', () => {
	it('returns a non-empty string for a valid ISO-8601 timestamp', () => {
		const result = formatTs('2024-01-15T10:30:00Z');
		expect(typeof result).toBe('string');
		expect(result.length).toBeGreaterThan(0);
	});

	it('produces output that includes the year', () => {
		const result = formatTs('2024-06-01T00:00:00Z');
		expect(result).toMatch(/2024/);
	});

	it('handles timestamps with milliseconds', () => {
		expect(() => formatTs('2024-01-01T12:00:00.000Z')).not.toThrow();
	});
});

describe('utilisationIntent', () => {
	it('returns "success" for 0%', () => {
		expect(utilisationIntent(0)).toBe('success');
	});

	it('returns "success" for values below 80', () => {
		expect(utilisationIntent(79.9)).toBe('success');
	});

	it('returns "warning" at exactly 80', () => {
		expect(utilisationIntent(80)).toBe('warning');
	});

	it('returns "warning" for values between 80 and 90', () => {
		expect(utilisationIntent(85)).toBe('warning');
	});

	it('returns "error" at exactly 90', () => {
		expect(utilisationIntent(90)).toBe('error');
	});

	it('returns "error" for values above 90', () => {
		expect(utilisationIntent(99.9)).toBe('error');
	});

	it('returns "error" for 100', () => {
		expect(utilisationIntent(100)).toBe('error');
	});
});
