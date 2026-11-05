using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahada.IntegrationTests.Support;

/// <summary>A signed-in person with an HTTP client that carries their token.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

/// <summary>A company with its owner, one customer with a site and a boiler at it.</summary>
public sealed record Firm(Person Owner, Guid Id, Guid CustomerId, Guid SiteId, Guid EquipmentId)
{
    public string Path => $"/api/companies/{Id}";
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static object SiteBody(string label = "Kazan dairesi") => new
    {
        label,
        address = "Cumhuriyet Cd. 12",
        city = "Samsun",
        latitude = 41.3335,
        longitude = 36.2714,
        accessNotes = "Anahtar kapıcıda.",
    };

    public static object OrderBody(Firm firm, string title = "Kombi su kaçırıyor", string priority = "Normal", string[]? checklist = null, string? skill = "hvac") => new
    {
        customerId = firm.CustomerId,
        siteId = firm.SiteId,
        equipmentId = firm.EquipmentId,
        title,
        description = "Alttan damlıyor, basınç düşüyor.",
        priority,
        requiredSkill = skill,
        estimatedMinutes = 90,
        checklist = checklist ?? ["Basıncı ölç", "Gaz kaçağı testi"],
    };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz")
    {
        using var anonymous = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static async Task<Firm> FirmAsync(this ApiFactory factory, string name = "Kuzey Klima")
    {
        var owner = await factory.RegisterAsync("Deniz Sahibi");
        var company = await (await owner.Http.PostAsJsonAsync("/api/companies", new { name })).ReadAsync<CompanyDto>();
        var path = $"/api/companies/{company.Id}";
        var customer = await (await owner.Http.PostAsJsonAsync($"{path}/customers", new { name = "Atakum Apartmanı", phone = "0362 000 00 00" })).ReadAsync<CustomerDto>();
        var site = await (await owner.Http.PostAsJsonAsync($"{path}/customers/{customer.Id}/sites", SiteBody())).ReadAsync<SiteDto>();
        var equipment = await owner.Http.PostAsJsonAsync($"{path}/customers/{customer.Id}/sites/{site.Id}/equipment",
            new { kind = "Kombi", brand = "Demirdöküm", model = "Nitromix", serialNumber = "DD-2291", installedOn = "2023-05-01", warrantyUntil = "2027-05-01" });
        Assert.True(equipment.IsSuccessStatusCode, await equipment.Content.ReadAsStringAsync());
        return new Firm(owner, company.Id, customer.Id, site.Id, (await equipment.ReadAsync<EquipmentDto>()).Id);
    }

    public static async Task AddMemberAsync(this Firm firm, Person member, string role)
    {
        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = member.Email, role });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task<WorkOrderDto> OrderAsync(this Firm firm, object? body = null, Person? by = null)
    {
        var response = await (by ?? firm.Owner).Http.PostAsJsonAsync($"{firm.Path}/work-orders", body ?? OrderBody(firm));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WorkOrderDto>();
    }

    public static async Task<WorkOrderDto> ScheduledAsync(this Firm firm, ApiFactory factory, Person technician, object? body = null)
    {
        var order = await firm.OrderAsync(body);
        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/schedule",
            new { technicianId = technician.Id, start = factory.Clock.GetUtcNow().AddHours(1) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WorkOrderDto>();
    }

    public static async Task<HttpResponseMessage> StatusAsync(this Person person, Firm firm, Guid orderId, string status, string? reason = null, string? resolution = null) =>
        await person.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{orderId}/status", new { status, reason, resolution });

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
