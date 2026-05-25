import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTagsService, type TagsMap } from '../Services/TagsService';

/** Query key factory for tags scoped to an allocation. */
export const tagKeys = {
	list: (allocationId: string) => ['tags', allocationId] as const,
};

/**
 * Query hook for GET /api/allocations/{allocationId}/tags.
 */
export function useTagsQuery(allocationId: string) {
	const service = useTagsService();
	return useQuery({
		queryKey: tagKeys.list(allocationId),
		queryFn: () => service.list(allocationId),
		enabled: !!allocationId,
	});
}

/**
 * Mutation hook for PUT /api/allocations/{allocationId}/tags. Performs a full
 * tag replace. Invalidates both the tag list and the allocation list (since
 * tags may be visible in the allocation table view).
 */
export function useReplaceTagsMutation(allocationId: string) {
	const service = useTagsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (tags: TagsMap) => service.replace(allocationId, tags),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: tagKeys.list(allocationId) });
			void queryClient.invalidateQueries({ queryKey: ['allocations'] });
		},
	});
}

/**
 * Mutation hook for DELETE /api/allocations/{allocationId}/tags/{key}.
 */
export function useDeleteTagMutation(allocationId: string) {
	const service = useTagsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (key: string) => service.deleteTag(allocationId, key),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: tagKeys.list(allocationId) });
			void queryClient.invalidateQueries({ queryKey: ['allocations'] });
		},
	});
}

/**
 * Composite hook that returns all tag-related hooks as a single object.
 * Use this when a component needs access to multiple tag operations without
 * importing each hook individually.
 */
export function useTags() {
	return {
		useTagsQuery,
		useReplace: useReplaceTagsMutation,
		useDelete: useDeleteTagMutation,
	};
}
