import { DefinitionTooltip } from '@carbon/react';

export function Rfc1918Term() {
	return (
		<DefinitionTooltip
			definition="RFC 1918 reserves three IPv4 ranges for private networks: 10.0.0.0/8, 172.16.0.0/12, and 192.168.0.0/16. These addresses are not routable on the public internet."
			openOnHover
		>
			RFC 1918
		</DefinitionTooltip>
	);
}
