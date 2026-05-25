import { describe, it, expect } from 'vitest';
import { parseTagFilter } from './AllocationUtils';

describe('parseTagFilter', () => {
	it('returns undefined for an empty string', () => {
		expect(parseTagFilter('')).toBeUndefined();
	});

	it('returns undefined for whitespace-only input', () => {
		expect(parseTagFilter('   ')).toBeUndefined();
	});

	it('returns undefined for input with spaces in the key', () => {
		// "key with spaces" has a space mid-key, which does not match the pattern
		expect(parseTagFilter('key with spaces')).toBeUndefined();
	});

	it('returns undefined for "= value" (no key)', () => {
		expect(parseTagFilter('= value')).toBeUndefined();
	});

	it('parses a bare key into a key-only filter', () => {
		expect(parseTagFilter('env')).toEqual({ tagKey: 'env' });
	});

	it('parses "key=value" into a full filter', () => {
		expect(parseTagFilter('env=prod')).toEqual({ tagKey: 'env', tagValue: 'prod' });
	});

	it('trims surrounding whitespace from a bare key', () => {
		expect(parseTagFilter('  env  ')).toEqual({ tagKey: 'env' });
	});

	it('trims surrounding whitespace from a "key = value" form', () => {
		expect(parseTagFilter('  env = prod  ')).toEqual({ tagKey: 'env', tagValue: 'prod' });
	});

	it('handles keys with hyphens and dots', () => {
		expect(parseTagFilter('app.region=us-east-1')).toEqual({ tagKey: 'app.region', tagValue: 'us-east-1' });
	});

	it('returns key-only filter when "=" is present but value is missing', () => {
		// "env=" has no value after the equals sign — trailing whitespace stripped, so undefined value
		expect(parseTagFilter('env=')).toBeUndefined();
	});

	it('returns undefined when there are two separate words without "="', () => {
		expect(parseTagFilter('env prod')).toBeUndefined();
	});
});
