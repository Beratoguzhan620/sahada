using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sahada.Api.Features.Demo;

/// <summary>
/// Keeps the public demo's company stocked, entirely through the API's own endpoints, so everything in it passes the
/// same rules as what a visitor does. On start it makes the company once: customers with sites and equipment around
/// Samsun, a parts catalog, four technicians on the board and the published sign-ins. Then, while the service is
/// awake, whenever the open jobs run low new ones come in: some booked into the technicians' next working hours, some
/// left unplanned for the visitor to dispatch, and one of each bot technician's jobs already under way.
/// </summary>
internal sealed partial class DemoWorker(
    IOptions<DemoOptions> options, IHttpClientFactory clients, IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DemoWorker> logger)
    : BackgroundService
{
    public const string HttpClientName = "Sahada.Demo";

    private const string CompanyName = "Karadeniz Klima Servis";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly (string Key, string Name, string[] Skills, string Base, double Lat, double Lng)[] Crew =
    [
        ("emre", "Emre Koç", ["air-conditioning", "electrical"], "Atakum depot", 41.3270, 36.2760),
        ("selin", "Selin Aydın", ["air-conditioning", "boiler"], "İlkadım depot", 41.2880, 36.3300),
        ("burak", "Burak Şen", ["boiler", "plumbing"], "Canik depot", 41.2700, 36.3600),
    ];

    private static readonly (string Name, string Phone, string Site, string Address, string City, double Lat, double Lng, string Kind, string Brand)[] Clients =
    [
        ("Liman Otel", "0362 431 10 10", "Main building", "Kale Mh. Liman Cd. 4", "İlkadım", 41.2945, 36.3405, "Air conditioner", "Daikin"),
        ("Atakum Diş Kliniği", "0362 437 20 20", "Clinic", "Atatürk Blv. 210", "Atakum", 41.3305, 36.2655, "Air conditioner", "Mitsubishi"),
        ("Yılmaz Apartmanı", "0362 238 30 30", "Boiler room", "Bahçelievler Sk. 12", "İlkadım", 41.2900, 36.3210, "Boiler", "Vaillant"),
        ("Canik Belediyesi", "0362 256 40 40", "Service building", "Yavuz Selim Cd. 1", "Canik", 41.2680, 36.3640, "Boiler", "Buderus"),
        ("Kurupelit Market", "0362 457 50 50", "Store", "Kurupelit Mh. 7", "Atakum", 41.3080, 36.3060, "Cold room", "Bitzer"),
        ("Deniz Kafe", "0362 431 60 60", "Café", "Cumhuriyet Mh. 15", "İlkadım", 41.2840, 36.3380, "Air conditioner", "Arçelik"),
    ];

    private static readonly (string Title, string Skill, string Priority, int Minutes, string[] Checklist)[] Jobs =
    [
        ("Annual maintenance", "air-conditioning", "Normal", 60, ["Clean filters", "Check refrigerant pressure", "Test drain"]),
        ("Not cooling", "air-conditioning", "High", 90, ["Check refrigerant pressure", "Inspect compressor"]),
        ("No hot water", "boiler", "Urgent", 60, ["Check pressure", "Read error code", "Test ignition"]),
        ("Boiler service before winter", "boiler", "Normal", 75, ["Clean burner", "Check flue", "Gas leak test"]),
        ("Leak under the unit", "plumbing", "High", 45, ["Find the leak", "Replace seal"]),
        ("Breaker trips when unit starts", "electrical", "High", 60, ["Measure current", "Check wiring"]),
        ("Strange noise from outdoor unit", "air-conditioning", "Low", 45, ["Check fan", "Tighten mounts"]),
    ];

    private readonly Random _random = new(7);
    private readonly Dictionary<string, Person> _crew = [];
    private Person _dispatcher = null!;
    private Guid _company;
    private DateTimeOffset _signedInAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        using var http = clients.CreateClient(HttpClientName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_company == Guid.Empty || clock.GetUtcNow() - _signedInAt > TimeSpan.FromMinutes(45))
                {
                    await SetUpAsync(http, stoppingToken);
                }

                await TopUpAsync(http, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The demo keeps going; the next round tries again.
                LogFailed(logger, ex);
            }

            await Task.Delay(options.Value.Every, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
        return started.Task.IsCompleted && !ct.IsCancellationRequested;
    }

    /// <summary>Signs everyone in, making the company and its people the first time.</summary>
    public async Task SetUpAsync(HttpClient http, CancellationToken ct)
    {
        var demo = options.Value;
        _dispatcher = await SignInAsync(http, demo.DispatcherEmail, "Deniz Yılmaz", ct);
        _crew["you"] = await SignInAsync(http, demo.TechnicianEmail, "Can Yıldız", ct);
        foreach (var member in Crew)
        {
            _crew[member.Key] = await SignInAsync(http, $"{member.Key}@technicians.sahada.demo", member.Name, ct);
        }

        var me = await SendAsync<Me>(http, HttpMethod.Get, "/api/me", _dispatcher.Token, null, ct);
        var company = me.Companies.FirstOrDefault(c => c.Name == CompanyName);
        if (company is null)
        {
            company = await SendAsync<Named>(http, HttpMethod.Post, "/api/companies", _dispatcher.Token, new { name = CompanyName }, ct);
            _company = company.Id;
            await CreateCompanyAsync(http, ct);
        }

        _company = company.Id;
        _signedInAt = clock.GetUtcNow();
        LogReady(logger, CompanyName);
    }

    private async Task CreateCompanyAsync(HttpClient http, CancellationToken ct)
    {
        var c = $"/api/companies/{_company}";
        var everyDay = Enum.GetValues<DayOfWeek>();
        var people = Crew.Select(m => (m.Key, m.Skills, m.Base, m.Lat, m.Lng))
            .Append(("you", new[] { "air-conditioning", "boiler", "plumbing", "electrical" }, "İlkadım depot", 41.2880, 36.3300));
        foreach (var (key, skills, baseLabel, lat, lng) in people)
        {
            var email = key == "you" ? options.Value.TechnicianEmail : $"{key}@technicians.sahada.demo";
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/members", _dispatcher.Token, new { email, role = "Technician" }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Put, $"{c}/technicians/{_crew[key].Id}", _dispatcher.Token, new
            {
                skills,
                baseLabel,
                baseLatitude = lat,
                baseLongitude = lng,
                shiftStart = "08:00:00",
                shiftEnd = "20:00:00",
                workDays = everyDay,
                active = true,
            }, ct);
        }

        foreach (var client in Clients)
        {
            var customer = await SendAsync<Named>(http, HttpMethod.Post, $"{c}/customers", _dispatcher.Token,
                new { name = client.Name, phone = client.Phone, email = (string?)null, notes = (string?)null }, ct);
            var site = await SendAsync<Named>(http, HttpMethod.Post, $"{c}/customers/{customer.Id}/sites", _dispatcher.Token, new
            {
                label = client.Site,
                address = client.Address,
                city = client.City,
                latitude = client.Lat,
                longitude = client.Lng,
                accessNotes = "Ask at the front desk.",
            }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/customers/{customer.Id}/sites/{site.Id}/equipment", _dispatcher.Token,
                new { kind = client.Kind, brand = client.Brand, model = (string?)null, serialNumber = (string?)null, installedOn = (string?)null, warrantyUntil = (string?)null }, ct);
        }

        foreach (var (sku, name, price) in new[]
                 {
                     ("FLT-100", "Air filter", 180m), ("GAS-R32", "R32 refrigerant (kg)", 650m), ("SEAL-20", "Drain seal", 45m),
                     ("IGN-01", "Boiler ignition electrode", 420m), ("CAP-35", "Compressor capacitor 35µF", 310m),
                 })
        {
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/parts", _dispatcher.Token, new { sku, name, unitPrice = price, active = true }, ct);
        }
    }

    /// <summary>When the open jobs run low, new ones come in: most booked, some for the visitor to dispatch.</summary>
    public async Task TopUpAsync(HttpClient http, CancellationToken ct)
    {
        var c = $"/api/companies/{_company}";
        var company = await SendAsync<CompanyInfo>(http, HttpMethod.Get, c, _dispatcher.Token, null, ct);
        var day = NextWorkingStart();
        var booked = await SendAsync<OrderPage>(http, HttpMethod.Get, $"{c}/work-orders?status=Scheduled&status=EnRoute&status=InProgress&pageSize=100", _dispatcher.Token, null, ct);
        var ahead = booked.Items.Count(o => o.ScheduledStart >= clock.GetUtcNow());

        // Enough on the board and some of it still ahead; a visitor coming back the next day finds a fresh day planned,
        // up to a limit, so a demo nobody opens for weeks does not pile up jobs.
        if ((company.OpenWorkOrders >= options.Value.OpenJobs && ahead > 0) || company.OpenWorkOrders >= options.Value.OpenJobs * 5)
        {
            return;
        }

        var customers = await SendAsync<CustomerPage>(http, HttpMethod.Get, $"{c}/customers?pageSize=50", _dispatcher.Token, null, ct);
        var free = _crew.ToDictionary(p => p.Key, _ => day);
        var started = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            var listed = customers.Items[_random.Next(customers.Items.Count)];
            var customer = await SendAsync<CustomerInfo>(http, HttpMethod.Get, $"{c}/customers/{listed.Id}", _dispatcher.Token, null, ct);
            var site = customer.Sites[0];
            var job = Jobs[_random.Next(Jobs.Length)];
            var order = await SendAsync<Named>(http, HttpMethod.Post, $"{c}/work-orders", _dispatcher.Token, new
            {
                customerId = customer.Id,
                siteId = site.Id,
                equipmentId = site.Equipment.FirstOrDefault()?.Id,
                title = job.Title,
                description = $"{customer.Name} called about this.",
                priority = job.Priority,
                requiredSkill = job.Skill,
                estimatedMinutes = job.Minutes,
                checklist = job.Checklist,
            }, ct);

            // Two of every six stay unplanned: the board suggests who should take them.
            if (i % 3 == 2)
            {
                continue;
            }

            // Every other booked job goes to the published technician, so signing in as them shows a day of work.
            var key = i % 2 == 0 ? "you" : Crew.First(m => m.Skills.Contains(job.Skill)).Key;
            var start = free[key];
            free[key] = start.AddMinutes(job.Minutes + 30);
            try
            {
                await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/work-orders/{order.Id}/schedule", _dispatcher.Token,
                    new { technicianId = _crew[key].Id, start }, ct);

                // A bot technician's first job of the round is already under way, so the board shows work in progress.
                if (key != "you" && start <= clock.GetUtcNow().AddHours(1) && started.Add(key))
                {
                    await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/work-orders/{order.Id}/status", _crew[key].Token,
                        new { status = "EnRoute", reason = (string?)null, resolution = (string?)null }, ct);
                }
            }
            catch (InvalidOperationException ex)
            {
                // The hour is already taken by an earlier round, or the job cannot start yet: it stays for the visitor to plan.
                LogNotBooked(logger, ex.Message);
            }
        }
    }

    /// <summary>The next half hour of today's working hours in Samsun (shifts end at 20:00), or tomorrow morning once the evening comes.</summary>
    private DateTimeOffset NextWorkingStart()
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var next = local.Date.AddHours(local.Hour).AddMinutes(local.Minute < 30 ? 30 : 60);
        var start = local.Hour >= 17
            ? today.AddDays(1).ToDateTime(new TimeOnly(9, 0))
            : next < local.Date.AddHours(9) ? today.ToDateTime(new TimeOnly(9, 0)) : next;
        return new DateTimeOffset(start, Zone.GetUtcOffset(start));
    }

    private async Task<Person> SignInAsync(HttpClient http, string email, string name, CancellationToken ct)
    {
        var password = options.Value.Password;
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
        using var register = login.IsSuccessStatusCode
            ? null
            : await http.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password }, ct);
        var response = register ?? login;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The demo account {email} could not sign in: {(int)response.StatusCode}.");
        }

        var session = (await response.Content.ReadFromJsonAsync<Session>(ct))!;
        return new Person(session.User.Id, session.AccessToken);
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default!
            : (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed record Person(Guid Id, string Token);

    private sealed record Session(string AccessToken, SessionUser User);

    private sealed record SessionUser(Guid Id);

    private sealed record Named(Guid Id, string Name);

    private sealed record Me(List<Named> Companies);

    private sealed record CompanyInfo(int OpenWorkOrders);

    private sealed record IdOnly(Guid Id);

    private sealed record SiteInfo(Guid Id, List<IdOnly> Equipment);

    private sealed record CustomerInfo(Guid Id, string Name, List<SiteInfo> Sites);

    private sealed record CustomerPage(List<IdOnly> Items);

    private sealed record OrderInfo(DateTimeOffset? ScheduledStart);

    private sealed record OrderPage(List<OrderInfo> Items);

    [LoggerMessage(Level = LogLevel.Information, Message = "The demo company {Company} is running")]
    private static partial void LogReady(ILogger log, string company);

    [LoggerMessage(Level = LogLevel.Information, Message = "A demo job was left unplanned: {Reason}")]
    private static partial void LogNotBooked(ILogger log, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A demo round failed")]
    private static partial void LogFailed(ILogger log, Exception exception);
}
