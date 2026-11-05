using Sahada.Core;
using Sahada.Core.Companies;

namespace Sahada.UnitTests;

public sealed class TeamTests
{
    [Theory]
    [InlineData(Role.Technician, Role.Technician, true)]
    [InlineData(Role.Technician, Role.Dispatcher, false)]
    [InlineData(Role.Dispatcher, Role.Dispatcher, true)]
    [InlineData(Role.Dispatcher, Role.Owner, false)]
    [InlineData(Role.Owner, Role.Dispatcher, true)]
    public void Each_role_includes_the_ones_before_it(Role role, Role needed, bool allowed) =>
        Assert.Equal(allowed, Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Builders.Now).Can(needed));

    [Fact]
    public void A_company_starts_with_its_creator_as_owner()
    {
        var (company, owner) = Company.Create("  Kuzey   Klima ", Guid.NewGuid(), Builders.Now);

        Assert.Equal("Kuzey Klima", company.Name);
        Assert.Equal("Europe/Istanbul", company.TimeZone);
        Assert.Equal((company.Id, Role.Owner), (owner.CompanyId, owner.Role));
    }

    [Fact]
    public void A_team_keeps_an_owner()
    {
        Company.EnsureAnOwnerRemains(1);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Company.EnsureAnOwnerRemains(0)).Code);
    }

    [Fact]
    public void Unknown_roles_are_refused() =>
        Assert.Throws<DomainException>(() => Membership.Create(Guid.NewGuid(), Guid.NewGuid(), (Role)7, Builders.Now));
}
