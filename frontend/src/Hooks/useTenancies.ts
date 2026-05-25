import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
	useTenanciesService,
	type CreateTenancyRequest,
	type UpdateTenancyRequest,
} from '../Services/TenanciesService';

/** Stable query key for the tenancy list. */
export const TENANCIES_KEY = ['tenancies'] as const;

/**
 * Query hook for GET /api/tenancies. Returns all tenancies visible to the
 * caller (GlobalAdmin only in practice).
 */
export function useTenanciesQuery() {
	const service = useTenanciesService();
	return useQuery({
		queryKey: TENANCIES_KEY,
		queryFn: () => service.list(),
	});
}

/**
 * Mutation hook for POST /api/tenancies. Invalidates the tenancy list on
 * success so the new row appears immediately.
 */
export function useCreateTenancyMutation() {
	const service = useTenanciesService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: CreateTenancyRequest) => service.create(request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: TENANCIES_KEY });
		},
	});
}

/**
 * Mutation hook for PUT /api/tenancies/{id}. Invalidates the tenancy list on
 * success so the updated row is reflected immediately.
 */
export function useUpdateTenancyMutation() {
	const service = useTenanciesService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: ({ id, request }: { id: string; request: UpdateTenancyRequest }) => service.update(id, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: TENANCIES_KEY });
		},
	});
}

/**
 * Mutation hook for DELETE /api/tenancies/{id}. Invalidates the tenancy list
 * and any user/subnet data that may reference the deleted tenancy.
 */
export function useDeleteTenancyMutation() {
	const service = useTenanciesService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (id: string) => service.delete(id),
		onSuccess: () => {
			// Invalidate the tenancy list and downstream resources that reference tenancies.
			void queryClient.invalidateQueries({ queryKey: TENANCIES_KEY });
			void queryClient.invalidateQueries({ queryKey: ['users'] });
			void queryClient.invalidateQueries({ queryKey: ['subnets'] });
		},
	});
}

/**
 * Composite hook that returns all tenancy-related hooks as a single object.
 * Use this when a component needs access to multiple tenancy operations without
 * importing each hook individually.
 */
export function useTenancies() {
	return {
		useTenanciesQuery,
		useCreate: useCreateTenancyMutation,
		useUpdate: useUpdateTenancyMutation,
		useDelete: useDeleteTenancyMutation,
	};
}
