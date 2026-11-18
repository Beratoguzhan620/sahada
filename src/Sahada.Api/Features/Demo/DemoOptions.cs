namespace Sahada.Api.Features.Demo;

/// <summary>
/// The public demo: an air-conditioning service company in Samsun with customers, technicians and a day of jobs on
/// the board, and two sign-ins it publishes (dispatcher and technician). Off everywhere else.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Where this API listens, for the demo's own calls.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>How often the board is topped up with new jobs while the service is awake.</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Fewer open jobs than this, and new ones come in.</summary>
    public int OpenJobs { get; set; } = 8;

    public string DispatcherEmail { get; set; } = "dispatcher@sahada.demo";

    public string TechnicianEmail { get; set; } = "technician@sahada.demo";

    /// <summary>Shared by every demo account; published on the sign-in page, so it guards nothing.</summary>
    public string Password { get; set; } = "sahada-demo-password";
}
