using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sahada.Api.Features.Demo;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    [Fact]
    public async Task Outside_the_public_demo_there_are_no_demo_sign_ins()
    {
        using var anonymous = factory.CreateClient();

        var accounts = await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/demo");

        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task The_demo_company_is_made_once_and_gets_a_board_of_jobs()
    {
        var demo = new DemoOptions { Enabled = true };
        var worker = new DemoWorker(Options.Create(demo), new TestClients(factory), factory.Services.GetRequiredService<IHostApplicationLifetime>(),
            factory.Clock, NullLogger<DemoWorker>.Instance);
        using var http = factory.CreateClient();

        await worker.SetUpAsync(http, CancellationToken.None);
        await worker.TopUpAsync(http, CancellationToken.None);
        // A restart finds the same company, and a full board gets nothing more.
        await worker.SetUpAsync(http, CancellationToken.None);
        await worker.TopUpAsync(http, CancellationToken.None);

        using var dispatcher = await SignedInAsync(http, demo.DispatcherEmail, demo.Password);
        var companies = (await dispatcher.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("companies");
        Assert.Equal(1, companies.GetArrayLength());
        var company = companies[0];
        Assert.Equal(6, company.GetProperty("openWorkOrders").GetInt32());
        Assert.Equal(5, company.GetProperty("members").GetInt32());

        var id = company.GetProperty("id").GetString();
        var scheduled = await dispatcher.GetFromJsonAsync<JsonElement>($"/api/companies/{id}/work-orders?status=New");
        Assert.Equal(2, scheduled.GetProperty("total").GetInt32());

        using var technician = await SignedInAsync(http, demo.TechnicianEmail, demo.Password);
        var theirs = (await technician.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("companies");
        Assert.Equal(1, theirs.GetArrayLength());
    }

    private async Task<HttpClient> SignedInAsync(HttpClient http, string email, string password)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class TestClients(ApiFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient();
    }
}
