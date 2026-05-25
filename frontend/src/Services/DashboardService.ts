import type FetchClient from '@leomylonas/json-fetch-client';
import { useMemo } from 'react';
import { z } from 'zod';
import { useFetchClient } from '../Utils/FetchClient';

// ── Shared schemas ────────────────────────────────────────────────────────────

/**
 * Aggregated IP utilisation statistics shared across both dashboard responses.
 * Mirrors SubnetUtilisationDto.
 */
export const subnetUtilisationSchema = z.object({
	totalIps: z.number(),
	allocatedIps: z.number(),
	freeIps: z.number(),
	excludedIps: z.number(),
	utilisationPercent: z.number(),
});
export type SubnetUtilisation = z.infer<typeof subnetUtilisationSchema>;

// ── GlobalAdmin dashboard schemas ─────────────────────────────────────────────

/** A subnet exceeding the exhaustion threshold, as seen by GlobalAdmin. */
export const globalExhaustionAlertSchema = z.object({
	subnetId: z.uuid(),
	cidr: z.string(),
	tenancyId: z.uuid().nullable(),
	tenancyName: z.string().nullable(),
	utilisationPercent: z.number(),
});
export type GlobalExhaustionAlert = z.infer<typeof globalExhaustionAlertSchema>;

/** A single audit entry row on the GlobalAdmin dashboard. */
export const globalDashboardAuditEntrySchema = z.object({
	id: z.uuid(),
	timestamp: z.string(),
	action: z.string(),
	userId: z.string(),
	performedBy: z.string(),
	tenancyId: z.uuid().nullable(),
	tenancyName: z.string().nullable(),
	detail: z.string().nullable(),
});
export type GlobalDashboardAuditEntry = z.infer<typeof globalDashboardAuditEntrySchema>;

/** Full response from GET /dashboard/global. */
export const globalDashboardSchema = z.object({
	tenancyCount: z.number(),
	userCount: z.number(),
	sharedSubnetCount: z.number(),
	sharedSubnetUtilisation: subnetUtilisationSchema,
	subnetsApproachingExhaustion: z.array(globalExhaustionAlertSchema),
	recentAuditEntries: z.array(globalDashboardAuditEntrySchema),
});
export type GlobalDashboard = z.infer<typeof globalDashboardSchema>;

// ── TenantAdmin dashboard schemas ─────────────────────────────────────────────

/** A subnet exceeding the exhaustion threshold, as seen by TenantAdmin. */
export const tenantExhaustionAlertSchema = z.object({
	subnetId: z.uuid(),
	cidr: z.string(),
	utilisationPercent: z.number(),
});
export type TenantExhaustionAlert = z.infer<typeof tenantExhaustionAlertSchema>;

/** A single audit entry row on the TenantAdmin dashboard. */
export const tenantDashboardAuditEntrySchema = z.object({
	id: z.uuid(),
	timestamp: z.string(),
	action: z.string(),
	userId: z.string(),
	performedBy: z.string(),
	detail: z.string().nullable(),
});
export type TenantDashboardAuditEntry = z.infer<typeof tenantDashboardAuditEntrySchema>;

/** Full response from GET /dashboard/tenant. */
export const tenantDashboardSchema = z.object({
	tenancyId: z.uuid(),
	tenancyName: z.string(),
	userCount: z.number(),
	privateSubnetCount: z.number(),
	privateSubnetUtilisation: subnetUtilisationSchema,
	accessibleSharedSubnetCount: z.number(),
	subnetsApproachingExhaustion: z.array(tenantExhaustionAlertSchema),
	recentAuditEntries: z.array(tenantDashboardAuditEntrySchema),
});
export type TenantDashboard = z.infer<typeof tenantDashboardSchema>;

// ── TenantUser dashboard schemas ──────────────────────────────────────────────

/** Summary of a recent allocation visible on the TenantUser dashboard. */
export const recentAllocationSchema = z.object({
	id: z.uuid(),
	ipAddress: z.string(),
	subnetCidr: z.string(),
	allocatedAt: z.string(),
	tags: z.record(z.string(), z.string()),
});
export type RecentAllocation = z.infer<typeof recentAllocationSchema>;

/** Summary of an accessible subnet visible on the TenantUser dashboard. */
export const accessibleSubnetSchema = z.object({
	subnetId: z.uuid(),
	cidr: z.string(),
	freeIps: z.number(),
});
export type AccessibleSubnet = z.infer<typeof accessibleSubnetSchema>;

/** Full response from GET /dashboard/user. */
export const userDashboardSchema = z.object({
	recentAccessibleAllocations: z.array(recentAllocationSchema),
	accessibleSubnets: z.array(accessibleSubnetSchema),
});
export type UserDashboard = z.infer<typeof userDashboardSchema>;

// ── Service ───────────────────────────────────────────────────────────────────

/**
 * Service class for the three role-specific dashboard endpoints.
 * The correct method must be called based on the authenticated user's role —
 * the backend will return 403 if the wrong endpoint is called.
 */
export class DashboardService {
	constructor(private readonly client: FetchClient) {}

	/**
	 * GET /dashboard/global — system-wide stats for GlobalAdmin. Includes
	 * tenant counts, shared subnet utilisation, exhaustion alerts across all
	 * subnets, and the 10 most recent audit entries.
	 */
	getGlobal(): Promise<GlobalDashboard> {
		return this.client.getJson('/dashboard/global', globalDashboardSchema);
	}

	/**
	 * GET /dashboard/tenant — tenancy-scoped stats for TenantAdmin. Includes
	 * user counts, private subnet utilisation, accessible shared subnet count,
	 * exhaustion alerts, and the 10 most recent audit entries for the tenancy.
	 */
	getTenant(): Promise<TenantDashboard> {
		return this.client.getJson('/dashboard/tenant', tenantDashboardSchema);
	}

	/**
	 * GET /dashboard/user — TenantUser view. Returns the accessible subnets
	 * with free IP counts and recent allocations made within the tenancy.
	 */
	getUser(): Promise<UserDashboard> {
		return this.client.getJson('/dashboard/user', userDashboardSchema);
	}
}

/**
 * Hook that returns a memoised DashboardService backed by the singleton FetchClient.
 */
export function useDashboardService(): DashboardService {
	const fetchClient = useFetchClient();
	return useMemo(() => new DashboardService(fetchClient), [fetchClient]);
}
