namespace Sahada.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string Conflict = "conflict";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
    public const string NotAssigned = "not_assigned";
    public const string DuplicateSku = "duplicate_sku";
    public const string NotInField = "not_in_field";
    public const string LacksSkill = "lacks_skill";
    public const string OutsideShift = "outside_shift";
    public const string SlotTaken = "slot_taken";
}
