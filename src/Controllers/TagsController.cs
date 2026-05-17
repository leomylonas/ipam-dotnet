using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages freeform key-value tags on IP allocations. Tags allow callers to
/// annotate allocations with arbitrary metadata and filter allocations by those
/// tags via the <c>GET /api/allocations</c> endpoint.
///
/// Tag keys must be unique per allocation. A <c>PUT</c> performs a full replace —
/// all existing tags are deleted and the supplied map is inserted.
///
/// All business logic and access control is delegated to <see cref="TagService"/>.
/// </summary>
[ApiController]
[Route("api/allocations/{id:guid}/tags")]
[Authorize]
public class TagsController : IpamControllerBase
{
	/// <summary>Service that owns all tag management business logic.</summary>
	private readonly TagService _tags;

	/// <summary>
	/// Initialises a new instance of <see cref="TagsController"/>.
	/// </summary>
	/// <param name="tags">Tag service, injected by the DI container.</param>
	public TagsController(TagService tags)
	{
		_tags = tags;
	}

	/// <summary>
	/// Returns all tags attached to the specified allocation.
	/// </summary>
	/// <param name="id">The ID of the allocation whose tags to list.</param>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="TagResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpGet]
	public Task<IActionResult> List(Guid id) =>
		ExecuteAsync(async () => Ok(await _tags.ListAsync(id, GetCaller())));

	/// <summary>
	/// Fully replaces all tags on the specified allocation with the supplied key-value map.
	/// </summary>
	/// <param name="id">The ID of the allocation whose tags to replace.</param>
	/// <param name="tags">A dictionary of key-value pairs to set as the new tag set.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpPut]
	public Task<IActionResult> Replace(Guid id, [FromBody] Dictionary<string, string> tags) =>
		ExecuteAsync(async () =>
		{
			await _tags.ReplaceAsync(id, tags, GetCaller());
			return NoContent();
		});

	/// <summary>
	/// Deletes a single tag from an allocation, identified by its key.
	/// </summary>
	/// <param name="id">The ID of the allocation from which to remove the tag.</param>
	/// <param name="key">The tag key to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation or tag does not exist.
	/// </returns>
	[HttpDelete("{key}")]
	public Task<IActionResult> DeleteTag(Guid id, string key) =>
		ExecuteAsync(async () =>
		{
			await _tags.DeleteTagAsync(id, key, GetCaller());
			return NoContent();
		});
}
