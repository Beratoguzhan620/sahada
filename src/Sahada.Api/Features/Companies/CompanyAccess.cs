using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Companies;

/// <summary>
/// The one gate in front of everything a company owns. Membership and role are read from the database on every
/// request, so a member who is removed or demoted loses access on their next click.
/// </summary>
internal static class CompanyAccess
{
    public static async Task<Membership> RequireAsync(SahadaDbContext db, Guid companyId, ClaimsPrincipal user, Role needed, CancellationToken ct)
    {
        var userId = user.UserId();
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct)
            // Not "forbidden": whether a company with this id exists is none of an outsider's business.
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no company with this id among yours.");

        if (!membership.Can(needed))
        {
            throw new DomainException(ErrorCodes.Forbidden, needed switch
            {
                Role.Owner => "Only the company's owners can do this.",
                _ => "Only dispatchers and owners can do this.",
            });
        }

        return membership;
    }

    /// <summary>Locks the company's row for the rest of the transaction, so changes to the team are checked one after the other.</summary>
    public static Task LockAsync(SahadaDbContext db, Guid companyId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM companies WHERE id = {companyId} FOR UPDATE", ct);
}
