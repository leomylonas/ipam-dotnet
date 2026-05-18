namespace IpamService.Config;

/// <summary>
/// Strongly-typed binding for the <c>Ui</c> section of <c>appsettings.json</c>.
/// Controls whether the React single-page application is served by the ASP.NET
/// Core host. Disabling this lets the API run in API-only mode without serving
/// any static files, which is useful in environments where the SPA is hosted
/// separately (e.g. a CDN or a dedicated static file server).
/// </summary>
public class UiOptions
{
	/// <summary>
	/// When <c>true</c> (the default), <c>app.UseStaticFiles()</c> and
	/// <c>app.MapFallbackToFile("index.html")</c> are registered so that
	/// the React SPA is served from the <c>wwwroot/</c> folder.
	/// When <c>false</c>, those registrations are skipped and the API
	/// continues to function normally without serving any static content.
	/// </summary>
	public bool Enabled { get; set; } = true;
}
