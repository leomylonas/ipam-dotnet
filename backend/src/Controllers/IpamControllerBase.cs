using System.Security.Claims;
using IpamService.Services;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Abstract base class shared by all IPAM API controllers. Provides two shared
/// helpers so that every controller action can stay thin:
/// <list type="bullet">
///   <item><description>
///     <see cref="GetCaller"/> extracts the authenticated user's identity and
///     tenancy context from HTTP claims into a <see cref="CallerContext"/> value
///     that can be passed to service methods.
///   </description></item>
///   <item><description>
///     <see cref="ExecuteAsync"/> wraps a service call and maps typed service
///     exceptions to the appropriate HTTP status codes, eliminating repetitive
///     try/catch blocks in every action method.
///   </description></item>
/// </list>
/// </summary>
public abstract class IpamControllerBase : ControllerBase
{
	/// <summary>
	/// Extracts the authenticated caller's context from the current request's
	/// claims principal. <c>BasicAuthHandler</c> places the user's Identity ID,
	/// role, and tenancy ID into the claims on every authenticated request, so
	/// this method is safe to call in any action covered by <c>[Authorize]</c>.
	/// </summary>
	/// <returns>
	/// A <see cref="CallerContext"/> describing who is making the request,
	/// which service methods use to enforce role-based access rules.
	/// </returns>
	protected CallerContext GetCaller() => new(
		// NameIdentifier is the ASP.NET Identity user ID (a GUID string).
		User.FindFirstValue(ClaimTypes.NameIdentifier)!,
		// Role claim is set to GlobalAdmin, TenantAdmin, or TenantUser.
		User.FindFirstValue(ClaimTypes.Role)!,
		// TenancyId is a custom claim added alongside the standard ones.
		// GlobalAdmin has no tenancy, so this may be null.
		Guid.TryParse(User.FindFirstValue(Auth.AuthConstants.Claims.TenancyId), out var g) ? g : null
	);

	/// <summary>
	/// Executes the supplied async action and catches typed service exceptions,
	/// mapping each to the HTTP response it represents. This eliminates
	/// boilerplate try/catch blocks from individual controller action methods.
	/// </summary>
	/// <param name="action">
	/// An async delegate that invokes one or more service methods and returns
	/// an <see cref="IActionResult"/>. Any typed service exception thrown inside
	/// it will be caught and translated to the correct HTTP response.
	/// </param>
	/// <returns>
	/// Either the <see cref="IActionResult"/> produced by <paramref name="action"/>
	/// or an error response derived from the caught exception type.
	/// </returns>
	protected async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
	{
		try
		{
			// Run the controller's business logic; propagate any non-service exceptions.
			return await action();
		}
		catch (NotFoundException ex)
		{
			// 404 — include a detail message only when the exception carries one,
			// so a bare NotFoundException() produces a minimal Problem response.
			return string.IsNullOrEmpty(ex.Message)
				? NotFound()
				: Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message);
		}
		catch (ForbiddenException)
		{
			// 403 — never reveal why access was denied; return only the status code.
			return Forbid();
		}
		catch (ConflictException ex)
		{
			// 409 — surface the business-rule conflict description as the detail field.
			return Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
		}
		catch (ValidationException ex)
		{
			// 400 — surface the validation failure description as the detail field.
			return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message);
		}
		catch (IdentityOperationException ex)
		{
			// 400 — surface ASP.NET Identity error descriptions (e.g. password policy
			// violations) as a structured extension on the Problem response.
			return Problem(
				statusCode: StatusCodes.Status400BadRequest,
				detail: "One or more Identity errors occurred.",
				extensions: new Dictionary<string, object?> { ["errors"] = ex.Errors });
		}
		catch (NoAvailableIpException ex)
		{
			// 409 — every usable address in the subnet is allocated or excluded.
			return Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
		}
		catch (NoContiguousBlockException ex)
		{
			// 409 — no run of the requested number of consecutive free addresses exists.
			return Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
		}
	}
}
