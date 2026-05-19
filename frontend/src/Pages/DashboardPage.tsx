import { Heading, Stack, Tile } from '@carbon/react';
import { useAuthStore } from '../Stores/AuthStore';
import { roleLabel } from '../Services/AuthService';

/**
 * Dashboard page — Phase 2 placeholder. Shows the authenticated user's profile
 * to confirm the auth flow and routing are wired up correctly.
 *
 * Phase 3 will replace this with role-scoped dashboard widgets that call
 * GET /dashboard/global, /dashboard/tenant, or /dashboard/user.
 */
export function DashboardPage() {
	const { user } = useAuthStore();

	// The appRoute's beforeLoad guard ensures user is never null here.
	if (user === null) {
		return null;
	}

	return (
		<Stack gap={6}>
			<Heading>Dashboard</Heading>
			<Tile>
				<Stack gap={3}>
					<p>
						<strong>Signed in as</strong> {user.username}
					</p>
					<p>
						<strong>Role</strong> {roleLabel[user.role] ?? user.role}
					</p>
					{user.tenancyId !== null && (
						<p>
							<strong>Tenancy</strong> {user.tenancyId}
						</p>
					)}
				</Stack>
			</Tile>
			<Tile>
				<p className="text-secondary">Dashboard widgets will be implemented in Phase 3.</p>
			</Tile>
		</Stack>
	);
}
