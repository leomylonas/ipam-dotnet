using IpamService.Data;
using IpamService.Models;

namespace IpamService.Services;

/// <summary>
/// Writes audit log entries to the database. Callers must call
/// <c>SaveChangesAsync</c> on the <see cref="AppDbContext"/> themselves —
/// this service only stages the new row so that it can be committed in the
/// same transaction as the business operation it records.
/// Registered as a scoped service so it shares the same EF context as the
/// controller that uses it.
/// </summary>
public class AuditService
{
	/// <summary>The EF context used to stage audit entries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="AuditService"/>.
	/// </summary>
	/// <param name="db">The EF Core context injected by the DI container.</param>
	public AuditService(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>
	/// Stages a new audit log entry in the current EF change-tracker.
	/// The entry is NOT persisted until the caller calls
	/// <c>_db.SaveChangesAsync()</c>, which allows the audit write to be
	/// part of the same database transaction as the mutation being recorded.
	/// </summary>
	/// <param name="userId">Identity ID of the acting user.</param>
	/// <param name="tenancyId">Tenancy context, or null for GlobalAdmin actions.</param>
	/// <param name="action">Short verb for the action, e.g. "Allocated", "Released".</param>
	/// <param name="ipAddress">The IP address involved, if applicable.</param>
	/// <param name="subnetId">The subnet involved, if applicable.</param>
	/// <param name="notes">Additional context to store alongside the entry.</param>
	public void Log(
		string userId,
		Guid? tenancyId,
		string action,
		string? ipAddress = null,
		Guid? subnetId = null,
		string? notes = null)
	{
		// Build the audit entry with the current UTC time and add it to the
		// EF change-tracker. It will be inserted when SaveChangesAsync is called.
		_db.AuditLogs.Add(new AuditLog
		{
			Id = Guid.NewGuid(),
			UserId = userId,
			TenancyId = tenancyId,
			Action = action,
			IpAddress = ipAddress,
			SubnetId = subnetId,
			Timestamp = DateTime.UtcNow,
			Notes = notes
		});
	}
}
