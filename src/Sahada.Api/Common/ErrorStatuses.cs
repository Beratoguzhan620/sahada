using Sahada.Core;

namespace Sahada.Api.Common;

/// <summary>Maps domain error codes to HTTP status codes. Unknown codes are business rule failures (422).</summary>
internal static class ErrorStatuses
{
    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidCredentials] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.InvalidState] = StatusCodes.Status409Conflict,
        [ErrorCodes.Conflict] = StatusCodes.Status409Conflict,
        [ErrorCodes.EmailTaken] = StatusCodes.Status409Conflict,
        [ErrorCodes.RateLimited] = StatusCodes.Status429TooManyRequests,
        [ErrorCodes.NotAssigned] = StatusCodes.Status403Forbidden,
        [ErrorCodes.DuplicateSku] = StatusCodes.Status409Conflict,
        [ErrorCodes.SlotTaken] = StatusCodes.Status409Conflict,
    };

    public static int StatusFor(string code) =>
        Statuses.TryGetValue(code, out var status) ? status : StatusCodes.Status422UnprocessableEntity;
}
