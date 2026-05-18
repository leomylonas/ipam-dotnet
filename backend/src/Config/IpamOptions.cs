namespace IpamService.Config;

/// <summary>
/// Strongly-typed binding for the <c>Seed</c> section of <c>appsettings.json</c>.
/// These credentials are used once on startup to bootstrap the GlobalAdmin user
/// if it does not already exist. After the first run the values are no longer
/// read — changing them will not update an existing admin's password.
/// </summary>
public class SeedOptions
{
	/// <summary>
	/// Username for the bootstrapped GlobalAdmin account.
	/// If a user with this name already exists the seed step is skipped.
	/// </summary>
	public string AdminUsername { get; set; } = string.Empty;

	/// <summary>
	/// Password for the bootstrapped GlobalAdmin account.
	/// Must satisfy the configured ASP.NET Identity password policy.
	/// </summary>
	public string AdminPassword { get; set; } = string.Empty;
}
