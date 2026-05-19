import { useNavigate, useRouterState } from '@tanstack/react-router';
import { SideNavLink } from '@carbon/react';

/** A single side-navigation link that uses TanStack Router for navigation. */
export function NavItem({ path, label, icon: Icon }: { path: string; label: string; icon: React.ComponentType }) {
	const navigate = useNavigate();
	const { location } = useRouterState();

	// Mark the link active when the current pathname starts with this path so
	// nested routes (e.g. /subnets/123) keep the parent nav item highlighted.
	const isActive = path === '/' ? location.pathname === '/' : location.pathname.startsWith(path);

	return (
		<SideNavLink
			href={path}
			renderIcon={Icon}
			isActive={isActive}
			onClick={(e: React.MouseEvent) => {
				// Prevent the browser from following the href (full-page reload)
				// and let TanStack Router handle the navigation client-side instead.
				e.preventDefault();
				void navigate({ to: path });
			}}
		>
			{label}
		</SideNavLink>
	);
}
