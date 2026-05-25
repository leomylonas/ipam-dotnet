import type { AllocationFilter } from '../Services/AllocationsService';

/**
 * Parses a search term into an AllocationFilter for tag-based filtering.
 * Accepts "key" (key-only) or "key=value" forms. Returns undefined for
 * blank or structurally invalid input (e.g. multiple "=" signs with spaces).
 */
export function parseTagFilter(term: string): AllocationFilter | undefined {
	const match = /^\s*([^=\s]+)\s*(?:=\s*(\S+))?\s*$/.exec(term);
	if (match === null) return undefined;
	const [, key, value] = match;
	return value !== undefined ? { tagKey: key, tagValue: value } : { tagKey: key };
}
