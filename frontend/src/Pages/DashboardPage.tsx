import { useAuthStore } from '../Stores/AuthStore';
import { Roles } from '../Services/AuthService';
import { Page } from './Page';
import { GlobalAdminDashboard } from '../Components/Dashboard/GlobalAdminDashboard';
import { TenantAdminDashboard } from '../Components/Dashboard/TenantAdminDashboard';
import { TenantUserDashboard } from '../Components/Dashboard/TenantUserDashboard';
import styles from './DashboardPage.module.scss';

export function DashboardPage() {
	const { user } = useAuthStore();

	if (user === null) {
		return null;
	}

	return (
		<Page>
			<h1 className={styles.pageTitle}>Dashboard</h1>
			{user.role === Roles.GlobalAdmin && <GlobalAdminDashboard />}
			{user.role === Roles.TenantAdmin && <TenantAdminDashboard />}
			{user.role === Roles.TenantUser && <TenantUserDashboard />}
		</Page>
	);
}
