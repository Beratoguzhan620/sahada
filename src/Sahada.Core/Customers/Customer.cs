namespace Sahada.Core.Customers;

/// <summary>Someone the company works for: a household, a shop, a factory. Work happens at their sites.</summary>
public sealed class Customer
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 30;
    public const int EmailMaxLength = 254;
    public const int NotesMaxLength = 2000;

    private Customer()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Notes { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Create(Guid companyId, string name, string? phone, string? email, string? notes, DateTimeOffset now)
    {
        var customer = new Customer { Id = Guid.CreateVersion7(now), CompanyId = companyId, CreatedAt = now };
        customer.Update(name, phone, email, notes);
        return customer;
    }

    public void Update(string name, string? phone, string? email, string? notes)
    {
        Name = Text.Clean(name, 2, NameMaxLength, "A customer's name");
        Phone = Optional(phone, PhoneMaxLength, "A phone number");
        Email = Optional(email, EmailMaxLength, "An email address").ToLowerInvariant();
        Notes = Text.CleanParagraphs(notes, NotesMaxLength, "Notes");
    }

    private static string Optional(string? value, int max, string what) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Text.Clean(value, 1, max, what);
}

/// <summary>A place where work is done, with what a technician needs to find and get into it.</summary>
public sealed class Site
{
    public const int LabelMaxLength = 60;
    public const int AddressMaxLength = 200;
    public const int CityMaxLength = 60;
    public const int AccessMaxLength = 500;

    private Site()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>Gate codes, where to park, who has the key.</summary>
    public string AccessNotes { get; private set; } = string.Empty;

    public static Site Create(Customer customer, string label, string address, string city, double latitude, double longitude, string? access, DateTimeOffset now)
    {
        var site = new Site { Id = Guid.CreateVersion7(now), CompanyId = customer.CompanyId, CustomerId = customer.Id };
        site.Update(label, address, city, latitude, longitude, access);
        return site;
    }

    public void Update(string label, string address, string city, double latitude, double longitude, string? access)
    {
        Label = Text.Clean(label, 1, LabelMaxLength, "A site's label");
        Address = Text.Clean(address, 3, AddressMaxLength, "An address");
        City = Text.Clean(city, 2, CityMaxLength, "A city");
        Latitude = latitude is >= -90 and <= 90 && double.IsFinite(latitude) ? latitude : throw new DomainException(ErrorCodes.ValidationFailed, "Latitude is between -90 and 90.");
        Longitude = longitude is >= -180 and <= 180 && double.IsFinite(longitude) ? longitude : throw new DomainException(ErrorCodes.ValidationFailed, "Longitude is between -180 and 180.");
        AccessNotes = Text.CleanParagraphs(access, AccessMaxLength, "Access notes");
    }
}

/// <summary>A unit at a site that gets serviced: a boiler, an air conditioner, a lift.</summary>
public sealed class Equipment
{
    public const int FieldMaxLength = 60;

    private Equipment()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid SiteId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public string Brand { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public string SerialNumber { get; private set; } = string.Empty;

    public DateOnly? InstalledOn { get; private set; }

    public DateOnly? WarrantyUntil { get; private set; }

    public static Equipment Create(Site site, string kind, string? brand, string? model, string? serial, DateOnly? installedOn, DateOnly? warrantyUntil, DateTimeOffset now)
    {
        var equipment = new Equipment { Id = Guid.CreateVersion7(now), CompanyId = site.CompanyId, SiteId = site.Id };
        equipment.Update(kind, brand, model, serial, installedOn, warrantyUntil);
        return equipment;
    }

    public void Update(string kind, string? brand, string? model, string? serial, DateOnly? installedOn, DateOnly? warrantyUntil)
    {
        Kind = Text.Clean(kind, 2, FieldMaxLength, "A kind of equipment");
        Brand = string.IsNullOrWhiteSpace(brand) ? string.Empty : Text.Clean(brand, 1, FieldMaxLength, "A brand");
        Model = string.IsNullOrWhiteSpace(model) ? string.Empty : Text.Clean(model, 1, FieldMaxLength, "A model");
        SerialNumber = string.IsNullOrWhiteSpace(serial) ? string.Empty : Text.Clean(serial, 1, FieldMaxLength, "A serial number");
        if (installedOn is { } installed && warrantyUntil is { } until && until < installed)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The warranty ends after the installation.");
        }

        InstalledOn = installedOn;
        WarrantyUntil = warrantyUntil;
    }

    public bool UnderWarranty(DateOnly today) => WarrantyUntil is { } until && today <= until;
}
