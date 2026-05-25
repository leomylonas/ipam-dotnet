import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
	useAllocationsService,
	type AllocateRequest,
	type BulkAllocateRequest,
	type AllocationFilter,
} from '../Services/AllocationsService';

/** Query key factory for allocations, supporting optional tag filter. */
export const allocationKeys = {
	list: (filter?: AllocationFilter) => ['allocations', filter ?? {}] as const,
};

/**
 * Query hook for GET /api/allocations. Accepts an optional tag filter.
 * GlobalAdmin sees all; TenantAdmin/User see their tenancy's allocations.
 */
export function useAllocationsQuery(filter?: AllocationFilter) {
	const service = useAllocationsService();
	return useQuery({
		queryKey: allocationKeys.list(filter),
		queryFn: () => service.list(filter),
	});
}

/**
 * Mutation hook for POST /api/allocations. Invalidates the allocation list
 * and the subnet stats (which change when a new IP is allocated).
 */
export function useAllocateMutation() {
	const service = useAllocationsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: AllocateRequest) => service.allocate(request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ['allocations'] });
			void queryClient.invalidateQueries({ queryKey: ['stats'] });
		},
	});
}

/**
 * Mutation hook for POST /api/allocations/bulk. Invalidates the same caches
 * as the single allocation mutation.
 */
export function useBulkAllocateMutation() {
	const service = useAllocationsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: BulkAllocateRequest) => service.bulkAllocate(request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ['allocations'] });
			void queryClient.invalidateQueries({ queryKey: ['stats'] });
		},
	});
}

/**
 * Mutation hook for DELETE /api/allocations/{id}. Invalidates the allocation
 * list and subnet stats on success.
 */
export function useReleaseAllocationMutation() {
	const service = useAllocationsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (id: string) => service.release(id),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ['allocations'] });
			void queryClient.invalidateQueries({ queryKey: ['stats'] });
		},
	});
}

/**
 * Composite hook that returns all allocation-related hooks as a single object.
 * Use this when a component needs access to multiple allocation operations without
 * importing each hook individually.
 */
export function useAllocations() {
	return {
		useAllocationsQuery,
		useAllocate: useAllocateMutation,
		useBulkAllocate: useBulkAllocateMutation,
		useRelease: useReleaseAllocationMutation,
	};
}
