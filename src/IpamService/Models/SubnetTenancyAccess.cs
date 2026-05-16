namespace IpamService.Models;

/// <summary>
/// Join table that restricts a Shared subnet to specific tenancies.
/// When at least one row exists for a subnet, only tenancies listed in those
/// rows may allocate from it. When no rows exist, the subnet is open to all
/// tenancies — this "open by default" semantic is intentional.
/// </summary>
public class SubnetTenancyAccess
{
	/// <summary>The shared subnet being restricted.</summary>
	public Guid SubnetId { get; set; }

	/// <summary>The tenancy being granted access to the subnet.</summary>
	public Guid TenancyId { get; set; }
}
