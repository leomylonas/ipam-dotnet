import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
	useSubnetsService,
	type CreateSubnetRequest,
	type UpdateSubnetRequest,
	type GrantSubnetAccessRequest,
} from '../Services/SubnetsService';

/** Query key factory for subnets — separates shared from private lists. */
export const subnetKeys = {
	shared: ['subnets', 'shared'] as const,
	private: (tenancyId: string) => ['subnets', 'private', tenancyId] as const,
	access: (subnetId: string) => ['subnets', 'access', subnetId] as const,
	all: ['subnets'] as const,
} as const;

// ── Shared subnets ────────────────────────────────────────────────────────────

/**
 * Query hook for GET /api/subnets/shared. Returns shared subnets accessible
 * to the caller's tenancy.
 */
export function useSharedSubnetsQuery() {
	const service = useSubnetsService();
	return useQuery({
		queryKey: subnetKeys.shared,
		queryFn: () => service.listShared(),
	});
}

/**
 * Mutation hook for POST /api/subnets/shared. Invalidates the shared subnet
 * list on success.
 */
export function useCreateSharedSubnetMutation() {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: CreateSubnetRequest) => service.createShared(request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.shared });
		},
	});
}

/**
 * Mutation hook for PUT /api/subnets/shared/{id}.
 */
export function useUpdateSharedSubnetMutation() {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: ({ id, request }: { id: string; request: UpdateSubnetRequest }) =>
			service.updateShared(id, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.shared });
		},
	});
}

/**
 * Mutation hook for DELETE /api/subnets/shared/{id}.
 */
export function useDeleteSharedSubnetMutation() {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (id: string) => service.deleteShared(id),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.shared });
		},
	});
}

/**
 * Mutation hook for POST /api/subnets/shared/{id}/access — restricts a shared
 * subnet to a specific tenancy.
 */
export function useGrantSubnetAccessMutation(subnetId: string) {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: GrantSubnetAccessRequest) => service.grantAccess(subnetId, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.shared });
			void queryClient.invalidateQueries({ queryKey: subnetKeys.access(subnetId) });
		},
	});
}

/**
 * Mutation hook for DELETE /api/subnets/shared/{id}/access/{tenancyId}.
 */
export function useRevokeSubnetAccessMutation(subnetId: string) {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (tenancyId: string) => service.revokeAccess(subnetId, tenancyId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.shared });
			void queryClient.invalidateQueries({ queryKey: subnetKeys.access(subnetId) });
		},
	});
}

/**
 * Query hook for GET /api/subnets/shared/{id}/access. Returns the tenancies
 * explicitly granted access to a shared subnet. Empty array = inaccessible to all.
 * Only enabled when subnetId is non-empty.
 */
export function useSubnetAccessQuery(subnetId: string) {
	const service = useSubnetsService();
	return useQuery({
		queryKey: subnetKeys.access(subnetId),
		queryFn: () => service.listAccess(subnetId),
		enabled: !!subnetId,
	});
}

// ── Private subnets ───────────────────────────────────────────────────────────

/**
 * Query hook for GET /api/tenancies/{tenancyId}/subnets. Returns private
 * subnets for the specified tenancy. The query is disabled when tenancyId is
 * empty (GlobalAdmin has no tenancy).
 */
export function usePrivateSubnetsQuery(tenancyId: string) {
	const service = useSubnetsService();
	return useQuery({
		queryKey: subnetKeys.private(tenancyId),
		queryFn: () => service.listPrivate(tenancyId),
		enabled: !!tenancyId,
	});
}

/**
 * Mutation hook for POST /api/tenancies/{tenancyId}/subnets.
 */
export function useCreatePrivateSubnetMutation(tenancyId: string) {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: CreateSubnetRequest) => service.createPrivate(tenancyId, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.private(tenancyId) });
		},
	});
}

/**
 * Mutation hook for PUT /api/tenancies/{tenancyId}/subnets/{subnetId}.
 */
export function useUpdatePrivateSubnetMutation(tenancyId: string) {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: ({ subnetId, request }: { subnetId: string; request: UpdateSubnetRequest }) =>
			service.updatePrivate(tenancyId, subnetId, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.private(tenancyId) });
		},
	});
}

/**
 * Mutation hook for DELETE /api/tenancies/{tenancyId}/subnets/{subnetId}.
 */
export function useDeletePrivateSubnetMutation(tenancyId: string) {
	const service = useSubnetsService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (subnetId: string) => service.deletePrivate(tenancyId, subnetId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: subnetKeys.private(tenancyId) });
		},
	});
}

/**
 * Composite hook that returns all subnet-related hooks as a single object,
 * covering both shared and private subnet operations.
 * Use this when a component needs access to multiple subnet operations without
 * importing each hook individually.
 */
export function useSubnets() {
	return {
		// Shared subnet hooks
		useSharedQuery: useSharedSubnetsQuery,
		useCreateShared: useCreateSharedSubnetMutation,
		useUpdateShared: useUpdateSharedSubnetMutation,
		useDeleteShared: useDeleteSharedSubnetMutation,
		useAccessQuery: useSubnetAccessQuery,
		useGrantAccess: useGrantSubnetAccessMutation,
		useRevokeAccess: useRevokeSubnetAccessMutation,
		// Private subnet hooks
		usePrivateQuery: usePrivateSubnetsQuery,
		useCreatePrivate: useCreatePrivateSubnetMutation,
		useUpdatePrivate: useUpdatePrivateSubnetMutation,
		useDeletePrivate: useDeletePrivateSubnetMutation,
	};
}
