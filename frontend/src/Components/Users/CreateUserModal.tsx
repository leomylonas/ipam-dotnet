import { useEffect } from 'react';
import { Modal, Form, Stack, TextInput, Select, SelectItem } from '@carbon/react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useUsers } from '../../Hooks/useUsers';
import { useTenancies } from '../../Hooks/useTenancies';
import { createUserSchema, type CreateUserRequest } from '../../Services/UsersService';
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

interface CreateUserModalProps {
	open: boolean;
	onClose: () => void;
	isGlobalAdmin: boolean;
}

export function CreateUserModal({ open, onClose, isGlobalAdmin }: CreateUserModalProps) {
	const { useCreate } = useUsers();
	const mutation = useCreate();
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
		watch,
		setValue,
		formState: { errors, isSubmitting },
	} = useForm<CreateUserRequest>({
		resolver: zodResolver(createUserSchema),
		defaultValues: {
			username: '',
			password: '',
			role: Roles.TenantUser,
			tenancyId: null,
		},
	});

	const selectedRole = watch('role');

	// Clear tenancyId whenever GlobalAdmin is selected — GlobalAdmin has no tenancy.
	useEffect(() => {
		if (selectedRole === Roles.GlobalAdmin) {
			setValue('tenancyId', null);
		}
	}, [selectedRole, setValue]);

	function handleClose() {
		reset();
		mutation.reset();
		onClose();
	}

	const onSubmit = handleSubmit(async (data) => {
		await mutation.mutateAsync(data);
		handleClose();
	});

	const roleOptions = isGlobalAdmin ? allRoleOptions : tenantRoleOptions;

	return (
		<Modal
			open={open}
			modalHeading="Create User"
			primaryButtonText={mutation.isPending ? 'Creating…' : 'Create'}
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
					<ErrorBanner error={mutation.error} title="Failed to create user" />
					<TextInput
						id="username"
						labelText="Username"
						placeholder="Login username"
						invalid={!!errors.username}
						invalidText={errors.username?.message}
						{...register('username')}
					/>
					<TextInput
						id="password"
						labelText="Password"
						type="password"
						placeholder="Initial password"
						invalid={!!errors.password}
						invalidText={errors.password?.message}
						{...register('password')}
					/>
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
					{isGlobalAdmin && selectedRole !== Roles.GlobalAdmin && (
						<Select
							id="tenancyId"
							labelText="Tenancy"
							invalid={!!errors.tenancyId}
							invalidText={errors.tenancyId?.message}
							{...register('tenancyId')}
						>
							<SelectItem value="" text="Select tenancy" />
							{tenancyOptions.map((opt) => (
								<SelectItem key={opt.value} value={opt.value} text={opt.label} />
							))}
						</Select>
					)}
				</Stack>
			</Form>
		</Modal>
	);
}
