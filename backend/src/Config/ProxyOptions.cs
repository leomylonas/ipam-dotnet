namespace IpamService.Config;

/// <summary>
/// Strongly-typed binding for the <c>Proxy</c> section of <c>appsettings.json</c>.
/// Controls how the application processes <c>X-Forwarded-For</c> and
/// <c>X-Forwarded-Proto</c> headers forwarded by a reverse proxy.
///
/// Equivalent environment-variable overrides use <c>__</c> as the separator:
/// <code>
/// Proxy__Enabled=false
/// Proxy__TrustAllProxies=true
/// </code>
/// </summary>
public class ProxyOptions
{
	/// <summary>
	/// Whether to register the <c>UseForwardedHeaders</c> middleware at all.
	/// Set to <c>false</c> for deployments where the app is directly
	/// internet-facing and no reverse proxy is present, to prevent clients
	/// from spoofing forwarded-header values.
	/// Defaults to <c>true</c>.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// When <c>true</c>, clears <c>KnownIPNetworks</c> and <c>KnownProxies</c>
	/// so that forwarded headers are accepted from any source address.
	/// Only set this in fully trusted network environments (e.g. a private
	/// Kubernetes cluster where the ingress IP is not predictable).
	/// When <c>false</c> (the default), only loopback addresses
	/// (<c>127.0.0.1</c> / <c>::1</c>) are trusted as proxy sources,
	/// unless additional addresses are supplied via <see cref="TrustedProxies"/>.
	/// Takes precedence over <see cref="TrustedProxies"/> when both are set.
	/// </summary>
	public bool TrustAllProxies { get; set; } = false;

	/// <summary>
	/// An explicit list of trusted proxy IP addresses or CIDR network ranges
	/// whose forwarded headers should be accepted. Each entry is either:
	/// <list type="bullet">
	///   <item><description>
	///     A plain IPv4 or IPv6 address (e.g. <c>10.0.0.1</c>, <c>fd00::1</c>)
	///     — added to <c>KnownProxies</c>.
	///   </description></item>
	///   <item><description>
	///     A CIDR range (e.g. <c>10.0.0.0/8</c>, <c>172.16.0.0/12</c>)
	///     — added to <c>KnownIPNetworks</c>.
	///   </description></item>
	/// </list>
	/// These addresses are added <em>on top of</em> the built-in loopback trust;
	/// they do not replace it. Invalid entries are logged as warnings and skipped.
	/// Ignored when <see cref="TrustAllProxies"/> is <c>true</c>.
	///
	/// Environment-variable syntax (ASP.NET Core array binding):
	/// <code>
	/// Proxy__TrustedProxies__0=10.0.0.1
	/// Proxy__TrustedProxies__1=172.16.0.0/12
	/// </code>
	/// </summary>
	public List<string> TrustedProxies { get; set; } = new();
}
