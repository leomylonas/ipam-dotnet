import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
	useExclusionsService,
	type CreateExclusionRequest,
	type UpdateExclusionRequest,
} from '../Services/ExclusionsService';
import { statsKeys } from './useStats';

/** Query key factory for exclusions scoped to a subnet. */
export const exclusionKeys = {
	list: (subnetId: string) => ['exclusions', subnetId] as const,
};

/**
 * Query hook for GET /api/subnets/{subnetId}/exclusions. Returns all exclusion
 * ranges for the given subnet.
 */
export function useExclusionsQuery(subnetId: string) {
	const service = useExclusionsService();
	return useQuery({
		queryKey: exclusionKeys.list(subnetId),
		queryFn: () => service.list(subnetId),
		enabled: !!subnetId,
	});
}

/**
 * Mutation hook for POST /api/subnets/{subnetId}/exclusions.
 */
export function useCreateExclusionMutation(subnetId: string) {
	const service = useExclusionsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: CreateExclusionRequest) => service.create(subnetId, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: exclusionKeys.list(subnetId) });
			void queryClient.invalidateQueries({ queryKey: statsKeys.subnet(subnetId) });
		},
	});
}

/**
 * Mutation hook for PUT /api/subnets/{subnetId}/exclusions/{id}.
 */
export function useUpdateExclusionMutation(subnetId: string) {
	const service = useExclusionsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: ({ id, request }: { id: string; request: UpdateExclusionRequest }) =>
			service.update(subnetId, id, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: exclusionKeys.list(subnetId) });
			void queryClient.invalidateQueries({ queryKey: statsKeys.subnet(subnetId) });
		},
	});
}

/**
 * Mutation hook for DELETE /api/subnets/{subnetId}/exclusions/{id}.
 */
export function useDeleteExclusionMutation(subnetId: string) {
	const service = useExclusionsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (id: string) => service.delete(subnetId, id),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: exclusionKeys.list(subnetId) });
			void queryClient.invalidateQueries({ queryKey: statsKeys.subnet(subnetId) });
		},
	});
}

/**
 * Composite hook that returns all exclusion-related hooks as a single object.
 * Use this when a component needs access to multiple exclusion operations without
 * importing each hook individually.
 */
export function useExclusions() {
	return {
		useExclusionsQuery,
		useCreate: useCreateExclusionMutation,
		useUpdate: useUpdateExclusionMutation,
		useDelete: useDeleteExclusionMutation,
	};
}
