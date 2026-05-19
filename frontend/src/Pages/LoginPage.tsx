import { useNavigate } from '@tanstack/react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Button, Form, Heading, InlineNotification, PasswordInput, Stack, TextInput, Tile } from '@carbon/react';
import { type LoginRequest, loginRequestSchema, useAuthService } from '../Services/AuthService';
import { useAuthStore } from '../Stores/AuthStore';
import styles from './LoginPage.module.scss';

/**
 * The login page. Presents a centred Carbon form that submits credentials to
 * POST /auth/login. On success, the auth store is updated with the returned
 * user profile and the router navigates to the dashboard.
 *
 * Form state and validation are managed by react-hook-form with a Zod resolver
 * so that field errors surface inline before a network request is ever made.
 */
export function LoginPage() {
	const authService = useAuthService();
	const { setUser } = useAuthStore();
	const navigate = useNavigate();

	const {
		register,
		handleSubmit,
		formState: { errors, isSubmitting },
		setError,
	} = useForm<LoginRequest>({
		resolver: zodResolver(loginRequestSchema),
	});

	const onSubmit = async (data: LoginRequest) => {
		try {
			const user = await authService.login(data);
			// Write the user into the global store so the router's beforeLoad guard
			// sees it synchronously on the immediately following navigate() call.
			setUser(user);
			void navigate({ to: '/' });
		} catch {
			// Do not reveal whether the username exists or the password was wrong
			// — use the same generic message for both failure modes.
			setError('root', { message: 'Invalid username or password.' });
		}
	};

	return (
		<div className={styles.page}>
			<div className={styles.card}>
				<Tile>
					<Stack gap={7}>
						<div>
							<Heading>IPAM</Heading>
							<p className={styles.subtitle}>IP Address Management</p>
						</div>
						{/* Error banner — only visible when login fails. */}
						{errors.root !== undefined && (
							<InlineNotification
								kind="error"
								title="Login failed"
								subtitle={errors.root.message}
								lowContrast
								hideCloseButton
							/>
						)}

						{/* Credentials form — validated against loginRequestSchema before submit. */}
						<Form onSubmit={(e) => void handleSubmit(onSubmit)(e)}>
							<Stack gap={5}>
								<TextInput
									id="username"
									labelText="Username"
									autoComplete="username"
									invalid={errors.username !== undefined}
									invalidText={errors.username?.message}
									autoFocus
									disabled={isSubmitting}
									{...register('username')}
								/>
								<PasswordInput
									id="password"
									labelText="Password"
									autoComplete="current-password"
									invalid={errors.password !== undefined}
									invalidText={errors.password?.message}
									disabled={isSubmitting}
									{...register('password')}
								/>
								<Button type="submit" disabled={isSubmitting} className="justify-self-end">
									{isSubmitting ? 'Signing in…' : 'Sign in'}
								</Button>
							</Stack>
						</Form>
					</Stack>
				</Tile>
			</div>
		</div>
	);
}
