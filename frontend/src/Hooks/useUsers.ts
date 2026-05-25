import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useUsersService, type CreateUserRequest, type UpdateUserRequest } from '../Services/UsersService';

/** Stable query key for the user list. */
export const USERS_KEY = ['users'] as const;

/**
 * Query hook for GET /api/users. Returns users visible to the caller —
 * GlobalAdmin sees all; TenantAdmin sees only their tenancy's users.
 */
export function useUsersQuery() {
	const service = useUsersService();
	return useQuery({
		queryKey: USERS_KEY,
		queryFn: () => service.list(),
	});
}

/**
 * Mutation hook for POST /api/users. Invalidates the user list on success.
 */
export function useCreateUserMutation() {
	const service = useUsersService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (request: CreateUserRequest) => service.create(request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: USERS_KEY });
		},
	});
}

/**
 * Mutation hook for PUT /api/users/{id}. Invalidates the user list on success.
 */
export function useUpdateUserMutation() {
	const service = useUsersService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: ({ id, request }: { id: string; request: UpdateUserRequest }) => service.update(id, request),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: USERS_KEY });
		},
	});
}

/**
 * Mutation hook for DELETE /api/users/{id}. Invalidates the user list on success.
 */
export function useDeleteUserMutation() {
	const service = useUsersService();
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (id: string) => service.delete(id),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: USERS_KEY });
		},
	});
}

/**
 * Composite hook that returns all user-related hooks as a single object.
 * Use this when a component needs access to multiple user operations without
 * importing each hook individually.
 */
export function useUsers() {
	return {
		useUsersQuery,
		useCreate: useCreateUserMutation,
		useUpdate: useUpdateUserMutation,
		useDelete: useDeleteUserMutation,
	};
}
