import { Page } from './Page';
import { useModal } from '../Hooks/useModal';
import { PageHeader } from '../Components/PageHeader/PageHeader';
import { UserList } from '../Components/Users/UserList';
import { CreateUserModal } from '../Components/Users/CreateUserModal';
import { useAuthStore } from '../Stores/AuthStore';
import { Roles } from '../Services/AuthService';

export function UsersPage() {
	const { user: caller } = useAuthStore();
	const isGlobalAdmin = caller?.role === Roles.GlobalAdmin;
	const createModal = useModal((onClose) => {
		return <CreateUserModal open onClose={onClose} isGlobalAdmin={isGlobalAdmin} />;
	});

	return (
		<Page>
			<PageHeader
				title="Users"
				description="Manage user accounts and role assignments."
				addLabel="Create User"
				onAdd={() => {
					createModal.open();
				}}
			/>
			<UserList />
			{createModal.modal}
		</Page>
	);
}
