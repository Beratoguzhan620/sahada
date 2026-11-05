using System.Net;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PartTests(ApiFactory factory)
{
    [Fact]
    public async Task Parts_have_unique_skus_within_a_company()
    {
        var firm = await factory.FirmAsync();
        var other = await factory.FirmAsync("Güney Isı");

        var first = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "vlv-22", name = "Emniyet ventili", unitPrice = 249.90m });
        var again = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "VLV-22", name = "Başka ventil", unitPrice = 10m });
        var elsewhere = await other.Owner.Http.PostAsJsonAsync($"{other.Path}/parts", new { sku = "VLV-22", name = "Emniyet ventili", unitPrice = 260m });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var part = await first.ReadAsync<PartDto>();
        Assert.Equal(("VLV-22", 249.90m), (part.Sku, part.UnitPrice));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("duplicate_sku", await again.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Retired_parts_drop_out_of_the_catalog()
    {
        var firm = await factory.FirmAsync();
        var filter = await (await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "FLT-1", name = "Su filtresi", unitPrice = 85m })).ReadAsync<PartDto>();
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "FLT-2", name = "Hava filtresi", unitPrice = 120m });

        var retire = await firm.Owner.Http.PutAsJsonAsync($"{firm.Path}/parts/{filter.Id}", new { sku = "FLT-1", name = "Su filtresi", unitPrice = 85m, active = false });
        var catalog = await (await firm.Owner.Http.GetAsync($"{firm.Path}/parts?q=filtre")).ReadAsync<PageDto<PartDto>>();
        var everything = await (await firm.Owner.Http.GetAsync($"{firm.Path}/parts?includeRetired=true")).ReadAsync<PageDto<PartDto>>();

        Assert.False((await retire.ReadAsync<PartDto>()).Active);
        Assert.Equal(["FLT-2"], catalog.Items.Select(p => p.Sku));
        Assert.Equal(2, everything.Total);
    }

    [Fact]
    public async Task Prices_are_to_the_kurus()
    {
        var firm = await factory.FirmAsync();

        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "X", name = "Vida", unitPrice = 0.125m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
