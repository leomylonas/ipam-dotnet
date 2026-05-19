import { SideNav, SideNavItems } from '@carbon/react';
import {
	Catalog,
	ChevronLeft,
	ChevronRight,
	Dashboard,
	Network_3,
	SubtractAlt,
	UserMultiple,
} from '@carbon/icons-react';
import { useAuthStore } from '../../Stores/AuthStore';
import { NavItem } from './NavItem';
import styles from './AppSideNav.module.scss';

interface AppSideNavProps {
	/** Whether the side navigation is expanded. */
	expanded: boolean;
	/** Called when the user clicks the collapse/expand toggle at the bottom. */
	onToggle: () => void;
}

/**
 * The application side navigation. Items are shown or hidden based on the
 * caller's role so that each role only sees pages they are permitted to visit.
 */
export function AppSideNav({ expanded, onToggle }: AppSideNavProps) {
	const { authStore } = useAuthStore();

	return (
		<SideNav
			aria-label="Side navigation"
			isRail
			expanded={expanded}
			addMouseListeners={false}
			addFocusListeners={false}
		>
			<SideNavItems>
				{/* Dashboard — visible to all authenticated roles. */}
				<NavItem path="/" label="Dashboard" icon={Dashboard} />

				{/* Tenancies — GlobalAdmin only. */}
				{authStore.isGlobalAdmin() && <NavItem path="/tenancies" label="Tenancies" icon={Network_3} />}

				{/* Users — GlobalAdmin and TenantAdmin. */}
				{(authStore.isGlobalAdmin() || authStore.isTenantAdmin()) && (
					<NavItem path="/users" label="Users" icon={UserMultiple} />
				)}

				{/* Shared subnets — GlobalAdmin only. */}
				{authStore.isGlobalAdmin() && (
					<NavItem path="/shared-subnets" label="Shared Subnets" icon={Network_3} />
				)}

				{/* Private subnets — GlobalAdmin and TenantAdmin. */}
				{(authStore.isGlobalAdmin() || authStore.isTenantAdmin()) && (
					<NavItem path="/subnets" label="Subnets" icon={SubtractAlt} />
				)}

				{/* Allocations — all roles. */}
				<NavItem path="/allocations" label="Allocations" icon={Dashboard} />

				{/* Audit log — GlobalAdmin and TenantAdmin. */}
				{(authStore.isGlobalAdmin() || authStore.isTenantAdmin()) && (
					<NavItem path="/audit" label="Audit Log" icon={Catalog} />
				)}
			</SideNavItems>

			<div className={styles.footer}>
				<button
					className={styles.toggle}
					onClick={onToggle}
					aria-label={expanded ? 'Collapse navigation' : 'Expand navigation'}
				>
					{expanded ? <ChevronLeft /> : <ChevronRight />}
					{expanded && <span>Collapse</span>}
				</button>
			</div>
		</SideNav>
	);
}
