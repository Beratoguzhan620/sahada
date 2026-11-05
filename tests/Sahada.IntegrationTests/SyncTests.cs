using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SyncTests(ApiFactory factory)
{
    /// <summary>The smallest valid JPEG header followed by filler; enough for the server, which only sniffs and stores.</summary>
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    private static async Task<PushDto> PushAsync(Person person, Firm firm, params object[] operations)
    {
        var response = await person.Http.PostAsJsonAsync($"{firm.Path}/sync/push", new { operations });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PushDto>();
    }

    private static async Task<PullDto> PullAsync(Person person, Firm firm, string? cursor = null) =>
        await (await person.Http.GetAsync($"{firm.Path}/sync/pull{(cursor is null ? "" : $"?cursor={cursor}")}")).ReadAsync<PullDto>();

    private static object Op(Guid orderId, string type, DateTimeOffset at, object? fields = null)
    {
        var op = new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["workOrderId"] = orderId, ["type"] = type, ["at"] = at };
        foreach (var property in fields?.GetType().GetProperties() ?? [])
        {
            op[property.Name] = property.GetValue(fields);
        }

        return op;
    }

    private async Task<(Firm Firm, Person Tech, WorkOrderDto Order, PartDto Part)> JobAsync()
    {
        var firm = await factory.FirmAsync();
        var part = await (await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/parts", new { sku = "VLV-22", name = "Emniyet ventili", unitPrice = 249.90m })).ReadAsync<PartDto>();
        var tech = await firm.TechnicianAsync(factory);
        var order = await firm.ScheduledAsync(factory, tech);
        // Some time passes after the job was opened, so offline work can be dated after it.
        factory.Clock.Advance(TimeSpan.FromHours(2));
        return (firm, tech, order, part);
    }

    [Fact]
    public async Task The_first_pull_brings_the_jobs_and_the_catalog_and_later_ones_only_changes()
    {
        var (firm, tech, order, _) = await JobAsync();
        await firm.OrderAsync();

        var first = await PullAsync(tech, firm);
        var quiet = await PullAsync(tech, firm, first.Cursor);
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/notes", new { text = "Müşteri 10:00'dan önce aramanızı istiyor." });
        var after = await PullAsync(tech, firm, quiet.Cursor);

        Assert.Equal([order.Id], first.Orders.Select(o => o.Id));
        Assert.Equal([order.Id], first.Assigned);
        Assert.Equal(["VLV-22"], first.Parts!.Select(p => p.Sku));
        Assert.Empty(quiet.Orders);
        Assert.Null(quiet.Parts);
        Assert.Equal("Note", Assert.Single(after.Orders).History[^1].Kind);
    }

    [Fact]
    public async Task A_bad_cursor_is_refused()
    {
        var (firm, tech, _, _) = await JobAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await tech.Http.GetAsync($"{firm.Path}/sync/pull?cursor=1;drop")).StatusCode);
    }

    [Fact]
    public async Task A_day_of_offline_work_lands_in_order()
    {
        var (firm, tech, order, part) = await JobAsync();
        var t = factory.Clock.GetUtcNow().AddMinutes(-90);
        var lineId = Guid.NewGuid();

        var push = await PushAsync(tech, firm,
            Op(order.Id, "StatusChanged", t, new { status = "EnRoute" }),
            Op(order.Id, "StatusChanged", t.AddMinutes(20), new { status = "InProgress" }),
            Op(order.Id, "ChecklistToggled", t.AddMinutes(30), new { itemId = order.Checklist[0].Id, done = true }),
            Op(order.Id, "ChecklistToggled", t.AddMinutes(35), new { itemId = order.Checklist[1].Id, done = true }),
            Op(order.Id, "PartUsed", t.AddMinutes(40), new { lineId, partId = part.Id, quantity = 2m }),
            Op(order.Id, "NoteAdded", t.AddMinutes(45), new { text = "Eski ventil paslanmış." }),
            Op(order.Id, "ResolutionSet", t.AddMinutes(50), new { text = "Emniyet ventili değişti, basınç 1.5 bar." }),
            Op(order.Id, "Signature", t.AddMinutes(55), new { signedBy = "Hasan Yılmaz", strokes = new[] { new[] { new[] { 0.1, 0.5 }, new[] { 0.4, 0.3 }, new[] { 0.9, 0.6 } } } }),
            Op(order.Id, "StatusChanged", t.AddMinutes(60), new { status = "Completed" }));

        Assert.All(push.Results, r => Assert.Equal("Applied", r.Outcome));
        var done = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Equal(("Completed", "Emniyet ventili değişti, basınç 1.5 bar."), (done.Status, done.Resolution));
        Assert.Equal(t.AddMinutes(20), done.StartedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(t.AddMinutes(60), done.CompletedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal((499.80m, 2m, "Emniyet ventili"), (done.PartsTotal, done.Parts[0].Quantity, done.Parts[0].Name));
        Assert.Equal(("Hasan Yılmaz", 3), (done.Signature!.SignedBy, done.Signature.Strokes[0].Count));
        Assert.Equal(0, done.Flagged);
        Assert.Contains(done.History, h => h.Kind == "PartUsed" && h.Detail == "2 × VLV-22 Emniyet ventili");
    }

    [Fact]
    public async Task Sending_the_same_operations_again_changes_nothing()
    {
        var (firm, tech, order, part) = await JobAsync();
        var now = factory.Clock.GetUtcNow();
        object[] ops =
        [
            Op(order.Id, "PartUsed", now, new { lineId = Guid.NewGuid(), partId = part.Id, quantity = 1m }),
            Op(order.Id, "NoteAdded", now, new { text = "Kapı kilitliydi." }),
            Op(order.Id, "StatusChanged", now, new { status = "OnHold" }),
        ];

        var first = await PushAsync(tech, firm, ops);
        var again = await PushAsync(tech, firm, ops);

        Assert.Equal(["Applied", "Applied", "Rejected"], first.Results.Select(r => r.Outcome));
        Assert.Equal(first.Results.Select(r => (r.Outcome, r.Message)), again.Results.Select(r => (r.Outcome, r.Message)));
        Assert.All(again.Results, r => Assert.True(r.Duplicate));
        var detail = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Single(detail.Parts);
        Assert.Single(detail.History, h => h.Kind == "Note");
    }

    [Fact]
    public async Task A_retry_racing_the_first_try_is_applied_once()
    {
        var (firm, tech, order, part) = await JobAsync();
        var op = Op(order.Id, "PartUsed", factory.Clock.GetUtcNow(), new { lineId = Guid.NewGuid(), partId = part.Id, quantity = 1m });

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PushAsync(tech, firm, op)));

        Assert.Single(results, r => !r.Results[0].Duplicate);
        Assert.All(results, r => Assert.Equal("Applied", r.Results[0].Outcome));
        Assert.Single((await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>()).Parts);
    }

    [Fact]
    public async Task Work_on_a_job_cancelled_meanwhile_keeps_the_facts_and_flags_them()
    {
        var (firm, tech, order, part) = await JobAsync();
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/cancel", new { reason = "Müşteri iptal etti" });
        var now = factory.Clock.GetUtcNow();

        var push = await PushAsync(tech, firm,
            Op(order.Id, "StatusChanged", now, new { status = "InProgress" }),
            Op(order.Id, "NoteAdded", now, new { text = "Geldim, kimse yok." }),
            Op(order.Id, "PartUsed", now, new { lineId = Guid.NewGuid(), partId = part.Id, quantity = 1m }));

        Assert.Equal(["Rejected", "Flagged", "Flagged"], push.Results.Select(r => r.Outcome));
        Assert.Equal("The job was cancelled meanwhile.", push.Results[0].Message);
        var detail = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Equal(("Cancelled", 2), (detail.Status, detail.Flagged));
        Assert.True(detail.Parts[0].Flagged);
    }

    [Fact]
    public async Task A_reassigned_job_leaves_the_old_device_and_its_late_work_is_flagged()
    {
        var (firm, ali, order, _) = await JobAsync();
        using var veli = await firm.TechnicianAsync(factory, "Veli");
        var before = await PullAsync(ali, firm);
        (await firm.ScheduleAsync(order.Id, veli, order.ScheduledStart!.Value)).EnsureSuccessStatusCode();
        var now = factory.Clock.GetUtcNow();

        var aliAfter = await PullAsync(ali, firm, before.Cursor);
        var veliPull = await PullAsync(veli, firm);
        var push = await PushAsync(ali, firm,
            Op(order.Id, "StatusChanged", now, new { status = "EnRoute" }),
            Op(order.Id, "NoteAdded", now, new { text = "Yoldaydım." }));

        Assert.Empty(aliAfter.Assigned);
        Assert.Equal([order.Id], veliPull.Assigned);
        Assert.Equal(["Rejected", "Flagged"], push.Results.Select(r => r.Outcome));
        Assert.Equal("The job was given to someone else meanwhile.", push.Results[0].Message);
    }

    [Fact]
    public async Task Someone_who_never_had_the_job_cannot_touch_it()
    {
        var (firm, _, order, _) = await JobAsync();
        using var stranger = await firm.TechnicianAsync(factory, "Başkası");

        var push = await PushAsync(stranger, firm, Op(order.Id, "NoteAdded", factory.Clock.GetUtcNow(), new { text = "Merhaba" }));
        var pull = await PullAsync(stranger, firm);

        Assert.Equal(("Rejected", "This job is not yours."), (push.Results[0].Outcome, push.Results[0].Message));
        Assert.Empty(pull.Orders);
    }

    [Fact]
    public async Task Checklist_ticks_are_last_write_wins_by_device_time()
    {
        var (firm, tech, order, _) = await JobAsync();
        var now = factory.Clock.GetUtcNow();
        var item = order.Checklist[0].Id;

        var push = await PushAsync(tech, firm,
            Op(order.Id, "ChecklistToggled", now.AddMinutes(-10), new { itemId = item, done = true }),
            Op(order.Id, "ChecklistToggled", now.AddMinutes(-30), new { itemId = item, done = false }));

        Assert.Equal(["Applied", "Stale"], push.Results.Select(r => r.Outcome));
        var detail = await (await tech.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.True(detail.Checklist[0].Done);
    }

    [Fact]
    public async Task Device_clocks_cannot_put_work_in_the_future()
    {
        var (firm, tech, order, _) = await JobAsync();
        var now = factory.Clock.GetUtcNow();

        await PushAsync(tech, firm, Op(order.Id, "StatusChanged", now.AddDays(3), new { status = "InProgress" }));

        var detail = await (await tech.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Equal(now, detail.StartedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Bad_operations_are_rejected_without_side_effects()
    {
        var (firm, tech, order, part) = await JobAsync();
        var now = factory.Clock.GetUtcNow();

        var push = await PushAsync(tech, firm,
            Op(order.Id, "PartUsed", now, new { lineId = Guid.NewGuid(), partId = part.Id, quantity = -1m }),
            Op(order.Id, "StatusChanged", now, new { status = "Completed", resolution = "Bitti" }),
            Op(Guid.NewGuid(), "NoteAdded", now, new { text = "?" }));

        Assert.All(push.Results, r => Assert.Equal("Rejected", r.Outcome));
        Assert.Equal("A work order that is scheduled cannot complete.", push.Results[1].Message);
        var detail = await (await tech.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Equal(("Scheduled", 0), (detail.Status, detail.Parts.Count));
    }

    [Fact]
    public async Task Photos_upload_once_under_the_devices_id_and_come_back()
    {
        var (firm, tech, order, _) = await JobAsync();
        var id = Guid.NewGuid();
        var path = $"{firm.Path}/work-orders/{order.Id}/photos/{id}";
        static ByteArrayContent Body(byte[] bytes) => new(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } };

        var first = await tech.Http.PutAsync(path, Body(Jpeg));
        var retry = await tech.Http.PutAsync(path, Body(Jpeg));
        var other = await tech.Http.PutAsync(path, Body([.. Jpeg, 42]));
        var notImage = await tech.Http.PutAsync($"{firm.Path}/work-orders/{order.Id}/photos/{Guid.NewGuid()}", Body("not a photo"u8.ToArray()));
        var caption = await PushAsync(tech, firm, Op(order.Id, "PhotoAdded", factory.Clock.GetUtcNow(), new { attachmentId = id, caption = "Paslı ventil" }));
        var download = await firm.Owner.Http.GetAsync(path);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest), (first.StatusCode, retry.StatusCode, other.StatusCode, notImage.StatusCode));
        Assert.Equal("Applied", caption.Results[0].Outcome);
        Assert.Equal(Jpeg, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType!.MediaType);
        var detail = await (await tech.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        Assert.Equal(("Paslı ventil", Jpeg.Length), (detail.Photos.Single().Caption, (int)detail.Photos.Single().SizeBytes));
    }

    [Fact]
    public async Task A_caption_for_a_photo_not_uploaded_yet_is_refused()
    {
        var (firm, tech, order, _) = await JobAsync();

        var push = await PushAsync(tech, firm, Op(order.Id, "PhotoAdded", factory.Clock.GetUtcNow(), new { attachmentId = Guid.NewGuid() }));

        Assert.Equal(("Rejected", "Upload the photo before sending this."), (push.Results[0].Outcome, push.Results[0].Message));
    }

    [Fact]
    public async Task Other_technicians_cannot_see_a_jobs_photos()
    {
        var (firm, tech, order, _) = await JobAsync();
        using var other = await firm.TechnicianAsync(factory, "Diğer");
        var id = Guid.NewGuid();
        (await tech.Http.PutAsync($"{firm.Path}/work-orders/{order.Id}/photos/{id}", new ByteArrayContent(Jpeg))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}/photos/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_service_report_is_a_pdf_with_the_job_and_the_parts()
    {
        var (firm, tech, order, part) = await JobAsync();
        var now = factory.Clock.GetUtcNow();
        await PushAsync(tech, firm,
            Op(order.Id, "StatusChanged", now, new { status = "InProgress" }),
            Op(order.Id, "PartUsed", now, new { lineId = Guid.NewGuid(), partId = part.Id, quantity = 1m }),
            Op(order.Id, "Signature", now, new { signedBy = "Hasan Yılmaz", strokes = new[] { new[] { new[] { 0.1, 0.1 }, new[] { 0.9, 0.9 } } } }));

        var response = await tech.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}/report");

        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains(order.Code, text);
        Assert.Contains("Emniyetventili", text.Replace(" ", ""));
        Assert.Contains("249,90TRY", text.Replace(" ", "").Replace(" ", ""));
        Assert.Contains("HasanYılmaz", text.Replace(" ", ""));
    }
}
