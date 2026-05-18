namespace IpamService.Config;

/// <summary>
/// Strongly-typed binding for the <c>Dashboard</c> section of <c>appsettings.json</c>.
/// Controls dashboard-level behaviour such as the utilisation threshold used to
/// flag subnets as approaching exhaustion on the dashboard views.
/// </summary>
public class DashboardOptions
{
	/// <summary>
	/// The utilisation percentage at or above which a subnet is flagged as
	/// approaching exhaustion on any dashboard view. Accepts values in the
	/// range 0–100. Defaults to <c>80.0</c> if not set in configuration.
	/// </summary>
	public double ExhaustionThresholdPercent { get; set; } = 80.0;
}
