using Sahada.Core;
using Sahada.Core.Customers;
using Sahada.Core.Parts;

namespace Sahada.UnitTests;

public sealed class CustomerTests
{
    [Fact]
    public void Customer_details_are_tidied()
    {
        var customer = Customer.Create(Guid.NewGuid(), "  Atakum   Apartmanı ", " 0362 000 ", " Yonetim@Example.com ", "Kapıcı: Ali\r\n\r\n\r\n\r\nZil 3", Builders.Now);

        Assert.Equal(("Atakum Apartmanı", "0362 000", "yonetim@example.com", "Kapıcı: Ali\n\nZil 3"), (customer.Name, customer.Phone, customer.Email, customer.Notes));
    }

    [Theory]
    [InlineData(91, 36)]
    [InlineData(41, -181)]
    [InlineData(double.NaN, 36)]
    public void Coordinates_must_be_on_the_map(double latitude, double longitude)
    {
        var customer = Customer.Create(Guid.NewGuid(), "Atakum Apartmanı", null, null, null, Builders.Now);

        var error = Assert.Throws<DomainException>(() => Site.Create(customer, "Ev", "Cumhuriyet Cd. 12", "Samsun", latitude, longitude, null, Builders.Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_site_belongs_to_its_customers_company()
    {
        var site = Builders.Site();

        Assert.NotEqual(Guid.Empty, site.CompanyId);
        Assert.Equal((41.33, 36.27), (site.Latitude, site.Longitude));
    }

    [Fact]
    public void Warranty_runs_to_the_end_of_its_last_day()
    {
        var equipment = Equipment.Create(Builders.Site(), "Kombi", "Demirdöküm", "Nitromix", "SN-1", new DateOnly(2024, 3, 1), new DateOnly(2026, 11, 2), Builders.Now);

        Assert.True(equipment.UnderWarranty(new DateOnly(2026, 11, 2)));
        Assert.False(equipment.UnderWarranty(new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void Warranty_cannot_end_before_installation() =>
        Assert.Throws<DomainException>(() => Equipment.Create(Builders.Site(), "Kombi", null, null, null, new DateOnly(2025, 1, 1), new DateOnly(2024, 1, 1), Builders.Now));

    [Fact]
    public void Part_prices_are_kept_in_kurus_and_skus_upper_case()
    {
        var part = Part.Create(Guid.NewGuid(), " vlv-22.b ", "Emniyet ventili", 249.90m, Builders.Now);

        Assert.Equal(("VLV-22.B", 24_990L, true), (part.Sku, part.UnitPriceMinor, part.Active));
    }

    [Theory]
    [InlineData("A B", 10)]
    [InlineData("SKU", -1)]
    [InlineData("SKU", 10.005)]
    public void Part_input_is_checked(string sku, double price) =>
        Assert.Throws<DomainException>(() => Part.Create(Guid.NewGuid(), sku, "Filtre", (decimal)price, Builders.Now));
}
