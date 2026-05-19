import Store, { useStoreValue, useStoreUpdate } from 'react-granular-store';
import { Roles, type AuthResponse } from '../Services/AuthService';

export class AuthStore extends Store<{
	user: AuthResponse | null;
	isCheckingAuth: boolean;
}> {
	constructor() {
		super({
			/** The current authenticated user, or null when unauthenticated. */
			user: null,
			/**
			 * True while the startup GET /auth/me is in flight. The Root component
			 * renders nothing until this resolves so the router does not briefly flash
			 * the login page before a valid existing session is confirmed.
			 */
			isCheckingAuth: true,
		});
	}

	public isAuthenticated() {
		return this.getState('user') !== null;
	}

	public isGlobalAdmin() {
		return this.getState('user')?.role === Roles.GlobalAdmin;
	}

	public isTenantAdmin() {
		return this.getState('user')?.role === Roles.TenantAdmin;
	}

	public isTenantUser() {
		return this.getState('user')?.role === Roles.TenantUser;
	}
}

/**
 * Global auth store. Holds the authenticated user's profile (or null when
 * unauthenticated) and a flag that gates rendering until the initial
 * GET /auth/me cookie-restoration check completes.
 *
 * This is a module-level singleton — no React Provider is required. Components
 * subscribe to individual keys via the hooks below.
 */
export const authStore = new AuthStore();

export function useAuthStore() {
	const user = useStoreValue(authStore, 'user');
	const setUser = useStoreUpdate(authStore, 'user');

	const isCheckingAuth = useStoreValue(authStore, 'isCheckingAuth');
	const setIsCheckingAuth = useStoreUpdate(authStore, 'isCheckingAuth');

	return {
		authStore,
		user,
		setUser,
		isCheckingAuth,
		setIsCheckingAuth,
	};
}
