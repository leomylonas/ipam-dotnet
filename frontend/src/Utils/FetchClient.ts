import FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';

/**
 * Singleton FetchClient instance shared across all API modules. No baseUrl is
 * set because Vite's dev-server proxy rewrites /api, /auth, and /dashboard
 * requests to the ASP.NET Core backend, keeping both on the same origin.
 *
 * The library default `credentials: 'include'` is preserved so the auth cookie
 * is sent on every request — this is the mechanism the React UI uses in place
 * of per-request Basic Auth headers.
 */
export const fetchClient = new FetchClient();

/**
 * Hook that returns a memoised reference to the singleton FetchClient. The
 * dependency array is empty so the same instance is always returned, making
 * hooks that depend on this (e.g. useAuth) stable across renders.
 */
export function useFetchClient(): FetchClient {
	return useMemo(() => fetchClient, []);
}
