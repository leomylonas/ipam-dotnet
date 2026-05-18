using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

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
	/// Returns audit log entries ordered newest-first. GlobalAdmin sees all entries;
	/// TenantAdmin sees only entries for their own tenancy; TenantUser is forbidden.
	/// </summary>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of <see cref="AuditLogResponse"/> objects, newest first.</returns>
	/// <exception cref="ForbiddenException">Thrown when the caller is a TenantUser.</exception>
	public async Task<List<AuditLogResponse>> ListAsync(CallerContext caller)
	{
		// Reject TenantUser callers up front — they have no audit log visibility.
		if (!caller.IsGlobalAdmin && !caller.IsTenantAdmin)
		{
			throw new ForbiddenException();
		}

		var query = _db.AuditLogs.AsQueryable();

		if (caller.IsTenantAdmin)
		{
			// Scope to the caller's tenancy; GlobalAdmin audit entries (TenancyId = null)
			// are excluded from tenant-level views.
			query = query.Where(a => a.TenancyId == caller.TenancyId);
		}

		// Return entries in reverse-chronological order so the most recent events
		// appear first, which is the natural order for an audit trail UI.
		return await query
			.OrderByDescending(a => a.Timestamp)
			.Select(a => new AuditLogResponse(a.Id, a.UserId, a.TenancyId, a.Action,
				a.IpAddress, a.SubnetId, a.Timestamp, a.Notes))
			.ToListAsync();
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
