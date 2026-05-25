import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Schemas ───────────────────────────────────────────────────────────────────

/**
 * Zod schema for a single audit log entry as returned by GET /api/audit.
 * Mirrors the AuditLogResponse DTO. Results arrive newest-first.
 */
export const auditLogSchema = z.object({
	id: z.uuid(),
	/** ASP.NET Identity user ID of the actor. */
	userId: z.string(),
	/** Tenancy context at the time of the action; null for GlobalAdmin actions. */
	tenancyId: z.uuid().nullable(),
	/** Short action verb, e.g. "Allocated", "Released", "SubnetCreated". */
	action: z.string(),
	/** IP address involved in the action, or null when not applicable. */
	ipAddress: z.string().nullable(),
	/** Subnet involved in the action, or null when not applicable. */
	subnetId: z.uuid().nullable(),
	/** UTC timestamp as an ISO-8601 string. */
	timestamp: z.string(),
	/** Optional extra context. */
	notes: z.string().nullable(),
});
export type AuditLog = z.infer<typeof auditLogSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/** Zod schema for a list of audit log entries. */
const auditLogListSchema = z.array(auditLogSchema);

/**
 * Service class for the audit log API. Audit entries are read-only —
 * they are written automatically by domain services and cannot be modified
 * or deleted through the API.
 */
export class AuditService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /api/audit — returns audit log entries, newest first.
	 * GlobalAdmin receives all entries; TenantAdmin receives only their tenancy's entries.
	 */
	list(): Promise<AuditLog[]> {
		return this.client.getJson('/api/audit', auditLogListSchema);
	}
}

/**
 * Hook that returns a memoised AuditService backed by the singleton FetchClient.
 */
export function useAuditService(): AuditService {
	const fetchClient = useFetchClient();
	return useMemo(() => new AuditService(fetchClient), [fetchClient]);
}
