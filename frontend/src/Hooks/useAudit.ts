import { useQuery } from '@tanstack/react-query';
import { useAuditService } from '../Services/AuditService';

/** Stable query key for the audit log list. */
export const AUDIT_KEY = ['audit'] as const;

/**
 * Query hook for GET /api/audit. Returns audit log entries newest-first.
 * GlobalAdmin receives all entries; TenantAdmin receives only their tenancy's.
 */
export function useAuditQuery() {
	const service = useAuditService();
	return useQuery({
		queryKey: AUDIT_KEY,
		queryFn: () => service.list(),
	});
}

/**
 * Composite hook that returns all audit-related hooks as a single object.
 * Use this when a component needs access to audit operations without importing
 * each hook individually.
 */
export function useAudit() {
	return {
		useAuditQuery,
	};
}
