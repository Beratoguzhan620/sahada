namespace Sahada.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record CompanyDto(Guid Id, string Name, string Role, string TimeZone, int Members, int Customers, int OpenWorkOrders, DateTimeOffset CreatedAt);

public sealed record MeDto(UserDto User, List<CompanyDto> Companies);

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);

public sealed record CustomerSummaryDto(Guid Id, string Name, string Phone, string Email, int Sites, int OpenWorkOrders, DateTimeOffset CreatedAt);

public sealed record EquipmentDto(Guid Id, Guid SiteId, string Kind, string Brand, string Model, string SerialNumber, DateOnly? InstalledOn, DateOnly? WarrantyUntil);

public sealed record SiteDto(Guid Id, Guid CustomerId, string Label, string Address, string City, double Latitude, double Longitude, string AccessNotes, List<EquipmentDto> Equipment);

public sealed record CustomerDto(Guid Id, string Name, string Phone, string Email, string Notes, List<SiteDto> Sites, DateTimeOffset CreatedAt);

public sealed record PartDto(Guid Id, string Sku, string Name, decimal UnitPrice, bool Active);

public sealed record WorkOrderSummaryDto(
    Guid Id, int Number, string Code, string Title, string Priority, string Status, Guid CustomerId, string CustomerName, Guid SiteId,
    string SiteLabel, string City, string RequiredSkill, Guid? TechnicianId, string? TechnicianName, DateTimeOffset? ScheduledStart,
    int EstimatedMinutes, DateTimeOffset DueAt, bool Overdue, DateTimeOffset CreatedAt);

public sealed record CustomerRefDto(Guid Id, string Name, string Phone);

public sealed record SiteRefDto(Guid Id, string Label, string Address, string City, double Latitude, double Longitude, string AccessNotes);

public sealed record EquipmentRefDto(Guid Id, string Kind, string Brand, string Model, string SerialNumber, DateOnly? WarrantyUntil, bool UnderWarranty);

public sealed record ChecklistItemDto(Guid Id, int Position, string Text, bool Done, DateTimeOffset? ChangedAt);

public sealed record HistoryDto(long Id, string Kind, Guid ActorId, string ActorName, DateTimeOffset At, string? FromStatus, string? ToStatus, string Detail);

public sealed record WorkOrderDto(
    Guid Id, int Number, string Code, string Title, string Description, string Priority, string Status, string RequiredSkill, int EstimatedMinutes,
    DateTimeOffset DueAt, bool Overdue, CustomerRefDto Customer, SiteRefDto Site, EquipmentRefDto? Equipment, Guid? TechnicianId,
    string? TechnicianName, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    string HoldReason, string CancelReason, string Resolution, List<ChecklistItemDto> Checklist, List<HistoryDto> History,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
