import { describe, it, expect, beforeEach } from 'vitest';
import { AuthStore } from './AuthStore';

// AuthStore is tested by instantiating it directly (bypassing the module
// singleton) so that each test starts from a clean, empty state.

let store: AuthStore;

beforeEach(() => {
	store = new AuthStore();
});

describe('AuthStore', () => {
	describe('isAuthenticated', () => {
		it('returns false when no user is set', () => {
			expect(store.isAuthenticated()).toBe(false);
		});

		it('returns true when a user is present', () => {
			store.setState('user', { id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null });
			expect(store.isAuthenticated()).toBe(true);
		});
	});

	describe('isGlobalAdmin', () => {
		it('returns false when no user is set', () => {
			expect(store.isGlobalAdmin()).toBe(false);
		});

		it('returns true for a GlobalAdmin user', () => {
			store.setState('user', { id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null });
			expect(store.isGlobalAdmin()).toBe(true);
		});

		it('returns false for a TenantAdmin user', () => {
			store.setState('user', {
				id: '2',
				username: 'tadmin',
				role: 'TenantAdmin',
				tenancyId: 'a1b2c3d4-0000-0000-0000-000000000001',
			});
			expect(store.isGlobalAdmin()).toBe(false);
		});
	});

	describe('isTenantAdmin', () => {
		it('returns false when no user is set', () => {
			expect(store.isTenantAdmin()).toBe(false);
		});

		it('returns true for a TenantAdmin user', () => {
			store.setState('user', {
				id: '2',
				username: 'tadmin',
				role: 'TenantAdmin',
				tenancyId: 'a1b2c3d4-0000-0000-0000-000000000001',
			});
			expect(store.isTenantAdmin()).toBe(true);
		});

		it('returns false for a GlobalAdmin user', () => {
			store.setState('user', { id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null });
			expect(store.isTenantAdmin()).toBe(false);
		});
	});

	describe('isTenantUser', () => {
		it('returns false when no user is set', () => {
			expect(store.isTenantUser()).toBe(false);
		});

		it('returns true for a TenantUser', () => {
			store.setState('user', {
				id: '3',
				username: 'user',
				role: 'TenantUser',
				tenancyId: 'a1b2c3d4-0000-0000-0000-000000000001',
			});
			expect(store.isTenantUser()).toBe(true);
		});

		it('returns false for a GlobalAdmin user', () => {
			store.setState('user', { id: '1', username: 'admin', role: 'GlobalAdmin', tenancyId: null });
			expect(store.isTenantUser()).toBe(false);
		});
	});
});
