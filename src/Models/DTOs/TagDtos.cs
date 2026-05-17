namespace IpamService.Models.DTOs;

/// <summary>
/// Response shape for a single allocation tag.
/// The PUT endpoint accepts a plain <c>Dictionary&lt;string, string&gt;</c> for
/// the full-replace operation, so there is no separate request DTO.
/// </summary>
/// <param name="Id">The tag's unique identifier.</param>
/// <param name="Key">The tag key. Unique within the allocation.</param>
/// <param name="Value">The tag value.</param>
public record TagResponse(
	Guid Id,
	string Key,
	string Value
);
