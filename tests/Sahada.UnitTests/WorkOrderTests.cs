using Sahada.Core;
using Sahada.Core.WorkOrders;

namespace Sahada.UnitTests;

public sealed class WorkOrderTests
{
    [Theory]
    [InlineData(Priority.Urgent, 4)]
    [InlineData(Priority.High, 24)]
    [InlineData(Priority.Normal, 72)]
    [InlineData(Priority.Low, 168)]
    public void The_deadline_follows_the_priority(Priority priority, int hours) =>
        Assert.Equal(Builders.Now.AddHours(hours), Builders.Order(priority).DueAt);

    [Fact]
    public void Raising_the_priority_moves_the_deadline_from_when_the_order_came_in()
    {
        var order = Builders.Order(Priority.Low);

        order.Edit(order.Title, order.Description, Priority.Urgent, order.RequiredSkill, order.EstimatedMinutes, Builders.Now.AddHours(1));

        Assert.Equal(Builders.Now.AddHours(4), order.DueAt);
    }

    [Fact]
    public void A_new_order_is_numbered_and_tidied()
    {
        var order = Builders.Order(Priority.Normal, "Basıncı ölç", "  Vanaları   kontrol et ");

        Assert.Equal(("SO-01001", WorkOrderStatus.New, "hvac"), (order.Code, order.Status, order.RequiredSkill));
        Assert.Equal(["Basıncı ölç", "Vanaları kontrol et"], order.Checklist.Select(i => i.Text));
        Assert.Equal([1, 2], order.Checklist.Select(i => i.Position));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(17 * 60)]
    public void Estimates_are_sensible(int minutes) =>
        Assert.Throws<DomainException>(() => Builders.Order().Edit("Kombi bakımı", null, Priority.Normal, null, minutes, Builders.Now));

    [Theory]
    [InlineData("two words")]
    [InlineData("a/b")]
    public void Skills_are_single_tags(string skill) =>
        Assert.Throws<DomainException>(() => Builders.Order().Edit("Kombi bakımı", null, Priority.Normal, skill, 60, Builders.Now));

    [Fact]
    public void The_happy_path_goes_from_new_to_completed()
    {
        var order = Builders.Order(Priority.Normal, "Basıncı ölç");
        var start = Builders.Now.AddHours(2);

        order.Schedule(Builders.Technician, start, Builders.Now);
        Assert.Equal((WorkOrderStatus.Scheduled, start.AddMinutes(90)), (order.Status, order.ScheduledEnd));
        order.SetOut(start);
        order.Start(start.AddMinutes(20), start.AddMinutes(20));
        order.SetChecklistItem(order.Checklist[0].Id, true, Builders.Technician, start.AddMinutes(30), start.AddMinutes(30));
        order.Complete("  Conta değişti, basınç 1.5 bar. ", start.AddMinutes(80), start.AddMinutes(80));

        Assert.Equal(WorkOrderStatus.Completed, order.Status);
        Assert.Equal(("Conta değişti, basınç 1.5 bar.", start.AddMinutes(20), start.AddMinutes(80)), (order.Resolution, order.StartedAt, order.CompletedAt));
        Assert.False(order.IsOpen);
    }

    [Fact]
    public void Work_cannot_start_before_it_is_scheduled()
    {
        var error = Assert.Throws<DomainException>(() => Builders.Order().Start(Builders.Now, Builders.Now));

        Assert.Equal(ErrorCodes.InvalidState, error.Code);
        Assert.Equal("A work order that is new cannot start.", error.Message);
    }

    [Fact]
    public void A_job_on_hold_keeps_its_first_start_time_when_it_resumes()
    {
        var order = Builders.Started();
        var started = order.StartedAt;

        order.Hold("Parça sipariş edildi", Builders.Now.AddHours(4));
        Assert.Equal((WorkOrderStatus.OnHold, "Parça sipariş edildi"), (order.Status, order.HoldReason));
        order.Start(Builders.Now.AddDays(1), Builders.Now.AddDays(1));

        Assert.Equal((WorkOrderStatus.InProgress, started, ""), (order.Status, order.StartedAt, order.HoldReason));
    }

    [Fact]
    public void A_job_on_hold_can_be_rescheduled()
    {
        var order = Builders.Started();
        order.Hold("Müşteri evde değil", Builders.Now.AddHours(4));

        order.Schedule(Builders.Technician, Builders.Now.AddDays(1), Builders.Now.AddHours(5));

        Assert.Equal(WorkOrderStatus.Scheduled, order.Status);
    }

    [Fact]
    public void Completing_needs_every_checklist_item_and_a_resolution()
    {
        var order = Builders.Started("Basıncı ölç", "Gaz kaçağı testi");

        var open = Assert.Throws<DomainException>(() => order.Complete("Tamam", Builders.Now, Builders.Now));
        Assert.Equal("2 checklist items are not done yet.", open.Message);

        foreach (var item in order.Checklist)
        {
            order.SetChecklistItem(item.Id, true, Builders.Technician, Builders.Now, Builders.Now);
        }

        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => order.Complete("  ", Builders.Now, Builders.Now)).Code);
    }

    [Fact]
    public void Ticking_twice_changes_nothing()
    {
        var order = Builders.Started("Basıncı ölç");
        var item = order.Checklist[0];

        Assert.True(order.SetChecklistItem(item.Id, true, Builders.Technician, Builders.Now.AddHours(4), Builders.Now.AddHours(4)));
        Assert.False(order.SetChecklistItem(item.Id, true, Builders.Dispatcher, Builders.Now.AddHours(5), Builders.Now.AddHours(5)));

        Assert.Equal((Builders.Technician, Builders.Now.AddHours(4)), (item.ChangedBy!.Value, item.ChangedAt!.Value));
    }

    [Fact]
    public void Finished_orders_are_frozen()
    {
        var order = Builders.Order();
        order.Cancel("Müşteri vazgeçti", Builders.Now);

        Assert.Equal("Müşteri vazgeçti", order.CancelReason);
        Assert.Throws<DomainException>(() => order.Cancel("Tekrar", Builders.Now));
        Assert.Throws<DomainException>(() => order.AddChecklistItem("Yeni adım", Builders.Now));
        Assert.Throws<DomainException>(() => order.Edit("Başka iş", null, Priority.Low, null, 60, Builders.Now));
    }

    [Fact]
    public void Unscheduling_returns_the_job_to_the_pile()
    {
        var order = Builders.Order();
        order.Schedule(Builders.Technician, Builders.Now.AddHours(1), Builders.Now);

        order.Unschedule(Builders.Now);

        Assert.Equal((WorkOrderStatus.New, (Guid?)null, (DateTimeOffset?)null), (order.Status, order.TechnicianId, order.ScheduledStart));
    }

    [Fact]
    public void Overdue_means_open_and_past_the_deadline()
    {
        var order = Builders.Order(Priority.Urgent);

        Assert.False(order.IsOverdue(Builders.Now.AddHours(4)));
        Assert.True(order.IsOverdue(Builders.Now.AddHours(4).AddSeconds(1)));
        order.Cancel("Gerek kalmadı", Builders.Now.AddHours(5));
        Assert.False(order.IsOverdue(Builders.Now.AddHours(6)));
    }

    [Fact]
    public void A_checklist_has_a_limit()
    {
        var order = Builders.Order(Priority.Normal, Enumerable.Range(1, WorkOrder.MaxChecklistItems).Select(i => $"Adım {i}").ToArray());

        Assert.Throws<DomainException>(() => order.AddChecklistItem("Bir adım daha", Builders.Now));
    }

    [Fact]
    public void Long_history_details_are_cut()
    {
        var order = Builders.Order();

        var entry = WorkOrderEvent.Record(order, WorkOrderEventKind.Edited, Builders.Dispatcher, Builders.Now, Builders.Now, new string('x', 5000));

        Assert.Equal(WorkOrderEvent.DetailMaxLength, entry.Detail.Length);
        Assert.Throws<DomainException>(() => WorkOrderEvent.Note(order, Builders.Dispatcher, " \n ", Builders.Now, Builders.Now));
    }
}
