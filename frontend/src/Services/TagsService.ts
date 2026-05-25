import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for a single allocation tag as returned by the API.
 * Mirrors the TagResponse DTO.
 */
export const tagSchema = z.object({
	id: z.uuid(),
	/** Tag key — unique within the allocation. */
	key: z.string(),
	value: z.string(),
});
export type Tag = z.infer<typeof tagSchema>;

/**
 * Schema for the full-replace tags body sent to PUT /api/allocations/{id}/tags.
 * A plain key-value map — all existing tags are replaced atomically.
 */
export const tagsMapSchema = z.record(z.string(), z.string());
export type TagsMap = z.infer<typeof tagsMapSchema>;

/**
 * Validation schema for a single tag entry in a form — used when the user
 * builds a tag list row by row in the UI before submitting the full map.
 */
export const tagEntrySchema = z.object({
	key: z.string().min(1, 'Key is required'),
	value: z.string().min(1, 'Value is required'),
});
export type TagEntry = z.infer<typeof tagEntrySchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of tags. */
const tagListSchema = z.array(tagSchema);

/**
 * Service class for allocation tag API operations.
 * Tags are freeform key-value pairs on allocations. Keys are unique per allocation.
 */
export class TagsService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/allocations/{allocationId}/tags — lists all tags on an allocation.
	 */
	list(allocationId: string): Promise<Tag[]> {
		return this.client.getJson(`/api/allocations/${allocationId}/tags`, tagListSchema);
	}

	/**
	 * PUT /api/allocations/{allocationId}/tags — fully replaces all tags on the
	 * allocation. Existing tags are deleted and replaced by the supplied map.
	 * Pass an empty object to clear all tags.
	 */
	replace(allocationId: string, tags: TagsMap): Promise<Tag[]> {
		return this.client.putJson(`/api/allocations/${allocationId}/tags`, tags, tagListSchema);
	}

	/**
	 * DELETE /api/allocations/{allocationId}/tags/{key} — deletes a single tag
	 * by its key.
	 */
	deleteTag(allocationId: string, key: string): Promise<void> {
		return this.client.deleteJson(`/api/allocations/${allocationId}/tags/${encodeURIComponent(key)}`);
	}
}

/**
 * Hook that returns a memoised TagsService backed by the singleton FetchClient.
 */
export function useTagsService(): TagsService {
	const fetchClient = useFetchClient();
	return useMemo(() => new TagsService(fetchClient), [fetchClient]);
}
