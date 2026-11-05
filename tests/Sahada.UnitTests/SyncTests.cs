using Sahada.Core;
using Sahada.Core.Parts;
using Sahada.Core.Sync;
using Sahada.Core.WorkOrders;

namespace Sahada.UnitTests;

public sealed class SyncTests
{
    private static readonly Guid Someone = Guid.NewGuid();

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(90, 90)]
    [InlineData(200, 120)]
    public void Device_times_are_clamped_between_the_jobs_start_and_now(int minutes, int expected)
    {
        var created = Builders.Now;
        var now = Builders.Now.AddMinutes(120);

        Assert.Equal(created.AddMinutes(expected), SyncRules.Clamp(created.AddMinutes(minutes), created, now));
    }

    [Fact]
    public void Very_old_device_times_go_back_a_week_at_most()
    {
        var now = Builders.Now.AddDays(30);

        Assert.Equal(now - SyncRules.MaxAge, SyncRules.Clamp(Builders.Now, Builders.Now, now));
    }

    [Theory]
    [InlineData(OperationType.StatusChanged, OperationOutcome.Applied)]
    [InlineData(OperationType.NoteAdded, OperationOutcome.Applied)]
    [InlineData(OperationType.PartUsed, OperationOutcome.Applied)]
    public void The_assigned_technician_works_freely(OperationType type, OperationOutcome expected) =>
        Assert.Equal(expected, SyncRules.Admit(type, Builders.Started(), Builders.Technician, dispatcher: false).Outcome);

    [Theory]
    [InlineData(OperationType.StatusChanged, OperationOutcome.Rejected)]
    [InlineData(OperationType.ChecklistToggled, OperationOutcome.Rejected)]
    [InlineData(OperationType.ResolutionSet, OperationOutcome.Rejected)]
    [InlineData(OperationType.NoteAdded, OperationOutcome.Flagged)]
    [InlineData(OperationType.PartUsed, OperationOutcome.Flagged)]
    [InlineData(OperationType.PartRemoved, OperationOutcome.Flagged)]
    [InlineData(OperationType.PhotoAdded, OperationOutcome.Flagged)]
    [InlineData(OperationType.Signature, OperationOutcome.Flagged)]
    public void On_a_cancelled_job_state_changes_are_refused_and_records_flagged(OperationType type, OperationOutcome expected)
    {
        var order = Builders.Started();
        order.Cancel("Müşteri vazgeçti", Builders.Now);

        Assert.Equal(expected, SyncRules.Admit(type, order, Builders.Technician, dispatcher: false).Outcome);
    }

    [Fact]
    public void The_previous_technicians_late_work_counts_as_a_record_only()
    {
        var order = Builders.Order();
        order.Schedule(Builders.Technician, Builders.Now.AddHours(1), Builders.Now);
        order.Schedule(Someone, Builders.Now.AddHours(1), Builders.Now);

        Assert.Equal(Builders.Technician, order.PreviousTechnicianId);
        Assert.Equal(OperationOutcome.Rejected, SyncRules.Admit(OperationType.StatusChanged, order, Builders.Technician, false).Outcome);
        Assert.Equal(OperationOutcome.Flagged, SyncRules.Admit(OperationType.NoteAdded, order, Builders.Technician, false).Outcome);
        Assert.Equal(OperationOutcome.Rejected, SyncRules.Admit(OperationType.NoteAdded, order, Guid.NewGuid(), false).Outcome);
    }

    [Fact]
    public void A_completed_job_takes_late_notes_but_no_state_changes()
    {
        var order = Builders.Started();
        order.Complete("Tamamlandı", Builders.Now.AddHours(4), Builders.Now.AddHours(4));

        Assert.Equal(OperationOutcome.Applied, SyncRules.Admit(OperationType.NoteAdded, order, Builders.Technician, false).Outcome);
        Assert.Equal("The job is already completed.", SyncRules.Admit(OperationType.ChecklistToggled, order, Builders.Technician, false).Reason);
    }

    [Fact]
    public void Dispatchers_may_act_for_anyone() =>
        Assert.Equal(OperationOutcome.Applied, SyncRules.Admit(OperationType.StatusChanged, Builders.Started(), Builders.Dispatcher, dispatcher: true).Outcome);

    [Fact]
    public void Completing_uses_the_resolution_written_while_working()
    {
        var order = Builders.Started();
        order.SetResolution("  Pompa değişti. ", Builders.Now.AddHours(4));

        order.Complete(null, Builders.Now.AddHours(5), Builders.Now.AddHours(5));

        Assert.Equal("Pompa değişti.", order.Resolution);
    }

    [Fact]
    public void Signatures_round_trip_as_strokes()
    {
        var order = Builders.Started();
        IReadOnlyList<IReadOnlyList<SignaturePoint>> strokes = [[new(0.1, 0.2), new(0.33333, 0.5)], [new(0.9, 0.9), new(1, 1)]];

        order.Sign(" Hasan  Yılmaz ", strokes, Builders.Now.AddHours(4), Builders.Now.AddHours(4));

        Assert.Equal(("Hasan Yılmaz", "0.1,0.2 0.333,0.5;0.9,0.9 1,1"), (order.SignedBy, order.Signature));
        IReadOnlyList<IReadOnlyList<SignaturePoint>> expected = [[new(0.1, 0.2), new(0.333, 0.5)], [new(0.9, 0.9), new(1, 1)]];
        Assert.Equal(expected, SignatureStrokes.Decode(order.Signature));
    }

    [Fact]
    public void Signatures_are_checked()
    {
        Assert.Throws<DomainException>(() => SignatureStrokes.Encode([[new(0.5, 0.5)]]));
        Assert.Throws<DomainException>(() => SignatureStrokes.Encode([[new(0.5, 0.5), new(1.5, 0.5)]]));
        Assert.Throws<DomainException>(() => Builders.Order().Sign("Hasan", [[new(0, 0), new(1, 1)]], Builders.Now, Builders.Now));
    }

    [Fact]
    public void Part_lines_copy_the_catalog_price_and_round_to_the_kurus()
    {
        var order = Builders.Started();
        var pipe = Part.Create(order.CompanyId, "PIPE-16", "Bakır boru (m)", 87.35m, Builders.Now);

        var line = PartLine.Create(Guid.NewGuid(), order, pipe, 2.5m, Builders.Technician, Builders.Now, flagged: false);
        pipe.Update("PIPE-16", "Bakır boru (m)", 99m, true);

        Assert.Equal((8735L, 21_838L), (line.UnitPriceMinor, line.TotalMinor));
        Assert.True(line.Remove(Builders.Now, flagged: true));
        Assert.False(line.Remove(Builders.Now, flagged: false));
        Assert.Equal((0L, true), (line.TotalMinor, line.Flagged));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000.5)]
    [InlineData(1.001)]
    public void Quantities_are_sensible(double quantity)
    {
        var order = Builders.Started();
        var part = Part.Create(order.CompanyId, "X", "Vida", 1m, Builders.Now);

        Assert.Throws<DomainException>(() => PartLine.Create(Guid.NewGuid(), order, part, (decimal)quantity, Builders.Technician, Builders.Now, false));
    }

    [Fact]
    public void Another_companys_part_cannot_be_used()
    {
        var order = Builders.Started();
        var part = Part.Create(Guid.NewGuid(), "X", "Vida", 1m, Builders.Now);

        Assert.Equal(ErrorCodes.NotFound, Assert.Throws<DomainException>(() => PartLine.Create(Guid.NewGuid(), order, part, 1, Builders.Technician, Builders.Now, false)).Code);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, null)]
    public void Photos_are_recognised_by_their_first_bytes(byte[] head, string? type) =>
        Assert.Equal(type, Attachment.Sniff(head));

    [Fact]
    public void Photos_are_kept_under_the_company_and_the_job()
    {
        var order = Builders.Started();
        var id = Guid.NewGuid();

        var photo = Attachment.Create(id, order, "image/png", 1024, new string('a', 64), Builders.Technician, Builders.Now, flagged: false);

        Assert.Equal($"companies/{order.CompanyId:N}/work-orders/{order.Id:N}/{id:N}.png", photo.StorageKey);
        Assert.Throws<DomainException>(() => Attachment.Create(id, order, "image/png", Attachment.MaxBytes + 1, "", Builders.Technician, Builders.Now, false));
        Assert.Throws<DomainException>(() => Attachment.Create(id, order, "image/gif", 10, "", Builders.Technician, Builders.Now, false));
    }

    [Fact]
    public void Adding_to_an_order_bumps_its_revision()
    {
        var order = Builders.Started();

        order.Touch(Builders.Now);
        order.Touch(Builders.Now);

        Assert.Equal(2, order.Revision);
    }
}
