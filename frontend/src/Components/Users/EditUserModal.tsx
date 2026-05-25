import { Modal, Form, Stack, TextInput, Select, SelectItem } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useUsers } from '../../Hooks/useUsers';
import { useTenancies } from '../../Hooks/useTenancies';
import { updateUserSchema, type User, type UpdateUserRequest } from '../../Services/UsersService';
import { Roles, roleLabel } from '../../Services/AuthService';
import { ErrorBanner } from '../Feedback/ErrorBanner';

interface SelectOption {
	value: string;
	label: string;
}

const allRoleOptions: SelectOption[] = [
	{ value: Roles.GlobalAdmin, label: roleLabel[Roles.GlobalAdmin] },
	{ value: Roles.TenantAdmin, label: roleLabel[Roles.TenantAdmin] },
	{ value: Roles.TenantUser, label: roleLabel[Roles.TenantUser] },
];

const tenantRoleOptions: SelectOption[] = [{ value: Roles.TenantUser, label: roleLabel[Roles.TenantUser] }];

interface EditUserModalProps {
	user: User | null;
	onClose: () => void;
	isGlobalAdmin: boolean;
	/** The authenticated caller's own user ID — used to hide the role field when editing self. */
	callerId: string | undefined;
}

export function EditUserModal({ user, onClose, isGlobalAdmin, callerId }: EditUserModalProps) {
	const { useUpdate } = useUsers();
	const mutation = useUpdate();
	const { useTenanciesQuery } = useTenancies();
	const { data: tenancies } = useTenanciesQuery();

	const tenancyOptions: SelectOption[] = (tenancies ?? []).map((t) => ({
		value: t.id,
		label: t.name,
	}));

	const {
		register,
		handleSubmit,
		reset,
		formState: { errors, isSubmitting },
	} = useForm<UpdateUserRequest>({
		resolver: zodResolver(updateUserSchema),
		values:
			user !== null
				? {
						username: user.username,
						role: user.role,
						tenancyId: user.tenancyId,
						password: '',
					}
				: undefined,
	});

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		if (user === null) {
			return;
		}
		// Omit empty password so the backend does not attempt a password change.
		const payload: UpdateUserRequest = {
			...data,
			password: data.password === '' ? undefined : data.password,
		};
		await mutation.mutateAsync({ id: user.id, request: payload });
		handleClose();
	});

	const roleOptions = isGlobalAdmin ? allRoleOptions : tenantRoleOptions;
	const isEditingSelf = user !== null && user.id === callerId;

	return (
		<Modal
			open={user !== null}
			modalHeading="Edit User"
			primaryButtonText={mutation.isPending ? 'Saving…' : 'Save'}
			primaryButtonDisabled={mutation.isPending || isSubmitting}
			secondaryButtonText="Cancel"
			onRequestSubmit={() => {
				void onSubmit();
			}}
			onRequestClose={handleClose}
			onSecondarySubmit={handleClose}
		>
			<Form>
				<Stack gap={5}>
					<ErrorBanner error={mutation.error} title="Failed to update user" />
					<TextInput
						id="username"
						labelText="Username"
						invalid={!!errors.username}
						invalidText={errors.username?.message}
						{...register('username')}
					/>
					{!isEditingSelf && (
						<Select
							id="role"
							labelText="Role"
							invalid={!!errors.role}
							invalidText={errors.role?.message}
							{...register('role')}
						>
							{roleOptions.map((opt) => (
								<SelectItem key={opt.value} value={opt.value} text={opt.label} />
							))}
						</Select>
					)}
					{isGlobalAdmin && (
						<Select
							id="tenancyId"
							labelText="Tenancy"
							invalid={!!errors.tenancyId}
							invalidText={errors.tenancyId?.message}
							{...register('tenancyId')}
						>
							<SelectItem value="" text="None (GlobalAdmin)" />
							{tenancyOptions.map((opt) => (
								<SelectItem key={opt.value} value={opt.value} text={opt.label} />
							))}
						</Select>
					)}
					<TextInput
						id="password"
						labelText="New password"
						type="password"
						placeholder="Leave blank to keep current password"
						helperText="Leave blank to keep the existing password."
						invalid={!!errors.password}
						invalidText={errors.password?.message}
						{...register('password')}
					/>
				</Stack>
			</Form>
		</Modal>
	);
}
