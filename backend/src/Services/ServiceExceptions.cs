using Microsoft.AspNetCore.Identity;

namespace IpamService.Services;

/// <summary>
/// Thrown by a service method when the requested resource does not exist.
/// Controllers catch this and return HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
	/// <summary>
	/// Initialises a new instance of <see cref="NotFoundException"/>.
	/// </summary>
	/// <param name="message">
	/// Optional human-readable message included in the response body.
	/// Pass <c>null</c> or omit to return a bare 404 with no body.
	/// </param>
	public NotFoundException(string? message = null) : base(message ?? string.Empty) { }
}

/// <summary>
/// Thrown by a service method when the caller is authenticated but lacks
/// the required role or tenancy membership to perform the operation.
/// Controllers catch this and return HTTP 403 Forbidden.
/// The message is intentionally not surfaced to the caller so that
/// resource existence cannot be inferred from error messages.
/// </summary>
public class ForbiddenException : Exception
{
	/// <summary>Initialises a new instance of <see cref="ForbiddenException"/>.</summary>
	public ForbiddenException() : base(string.Empty) { }
}

/// <summary>
/// Thrown by a service method when a business rule conflict prevents the
/// operation — for example, a duplicate name or an overlapping CIDR.
/// Controllers catch this and return HTTP 409 Conflict with the message as the body.
/// </summary>
public class ConflictException : Exception
{
	/// <summary>
	/// Initialises a new instance of <see cref="ConflictException"/>.
	/// </summary>
	/// <param name="message">Human-readable description of the conflict, returned in the response body.</param>
	public ConflictException(string message) : base(message) { }
}

/// <summary>
/// Thrown by a service method when a supplied value is malformed or cannot be
/// interpreted — for example, an invalid CIDR string or an IP address that fails
/// parsing. Unlike <see cref="System.ComponentModel.DataAnnotations.ValidationException"/>,
/// this is not tied to a named request field. Controllers catch this and return
/// HTTP 400 Bad Request with the message as the <c>detail</c> of a Problem Details response.
/// </summary>
public class BadValueException : Exception
{
	/// <summary>
	/// Initialises a new instance of <see cref="BadValueException"/>.
	/// </summary>
	/// <param name="message">Human-readable description of the bad value.</param>
	public BadValueException(string message) : base(message) { }
}

/// <summary>
/// Thrown when an ASP.NET Identity operation (CreateAsync, UpdateAsync,
/// AddPasswordAsync, etc.) returns one or more errors. Controllers catch this
/// and return HTTP 400 Bad Request with the error descriptions as the body,
/// so callers receive actionable feedback (e.g. "Passwords must have at least
/// one uppercase character").
/// </summary>
public class IdentityOperationException : Exception
{
	/// <summary>
	/// The Identity errors to surface to the API caller.
	/// Each corresponds to one <c>IdentityError</c> from
	/// the failed Identity operation.
	/// </summary>
	public IEnumerable<IdentityError> Errors { get; }

	/// <summary>
	/// Initialises a new instance of <see cref="IdentityOperationException"/>.
	/// </summary>
	/// <param name="errors">The Identity error descriptions from the failed operation.</param>
	public IdentityOperationException(IEnumerable<IdentityError> errors)
		: base(string.Join("; ", errors.Select(e => e.Description)))
	{
		Errors = errors;
	}
}
