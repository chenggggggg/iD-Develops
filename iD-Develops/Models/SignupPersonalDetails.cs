using System.Net.Mail;
using iD_Develops.Enums;

namespace iD_Develops.Models;

public sealed class SignupPersonalDetails
{
    public const string FullNameKey = "signup_full_name";
    public const string EmailKey = "signup_email";
    public static IReadOnlyList<(string Property, string Key, int MaxLength, string LabelKey)> OptionalFields { get; } =
    [
        (nameof(Phone), "signup_phone", 50, "SignupPhoneLabel"),
        (nameof(StreetAddress), "signup_street_address", 200, "SignupStreetAddressLabel"),
        (nameof(PostalCode), "signup_postal_code", 20, "SignupPostalCodeLabel"),
        (nameof(City), "signup_city", 100, "SignupCityLabel"),
        (nameof(Country), "signup_country", 100, "SignupCountryLabel")
    ];

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? StreetAddress { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    public void Normalize()
    {
        FullName = FullName?.Trim() ?? string.Empty;
        Email = Email?.Trim() ?? string.Empty;
        Phone = Phone?.Trim() ?? string.Empty;
        StreetAddress = StreetAddress?.Trim() ?? string.Empty;
        PostalCode = PostalCode?.Trim() ?? string.Empty;
        City = City?.Trim() ?? string.Empty;
        Country = Country?.Trim() ?? string.Empty;
    }

    public Dictionary<string, string> ToValues() => new()
    {
        [FullNameKey] = FullName,
        [EmailKey] = Email,
        ["signup_phone"] = Phone ?? string.Empty,
        ["signup_street_address"] = StreetAddress ?? string.Empty,
        ["signup_postal_code"] = PostalCode ?? string.Empty,
        ["signup_city"] = City ?? string.Empty,
        ["signup_country"] = Country ?? string.Empty
    };

    public static SignupPersonalDetails FromValues(IDictionary<string, string> values)
    {
        string Read(string key) => values.TryGetValue(key, out var value) ? value : string.Empty;
        return new()
        {
            FullName = Read(FullNameKey),
            Email = Read(EmailKey),
            Phone = Read("signup_phone"),
            StreetAddress = Read("signup_street_address"),
            PostalCode = Read("signup_postal_code"),
            City = Read("signup_city"),
            Country = Read("signup_country")
        };
    }

    public static bool IsPersonalKey(string key)
        => key == FullNameKey || key == EmailKey || OptionalFields.Any(item => item.Key == key);

    public static bool IsValidOptionalValue(string? value, int maxLength)
        => value == null || value.Length <= maxLength && !value.Any(char.IsControl);

    public static bool IsValidName(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200 &&
           !value.Any(char.IsControl);

    public static bool IsValidEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 254 &&
           !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var address) &&
           string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase) &&
           new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value);

    // Keep legacy fields in storage for historical records, but replace clearly identified
    // personal questions with the built-in personal fields in signup interfaces.
    public static bool IsPersonalField(CatalogProductFormField field)
    {
        if (field.IsPerParticipant) return false;
        static string Normalize(string? text) => new((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var key = Normalize(field.Key);
        var label = Normalize(field.Label);
        return field.FieldType switch
        {
            CatalogFormFieldType.Email => IsEmail(key) || IsEmail(label) || field.Translations.Any(item => IsEmail(Normalize(item.Label))),
            CatalogFormFieldType.Text => IsName(key) || IsName(label) || IsContact(key) || IsContact(label) ||
                field.Translations.Any(item => IsName(Normalize(item.Label)) || IsContact(Normalize(item.Label))),
            CatalogFormFieldType.Tel => IsPhone(key) || IsPhone(label) || field.Translations.Any(item => IsPhone(Normalize(item.Label))),
            CatalogFormFieldType.Textarea => IsAddress(key) || IsAddress(label),
            CatalogFormFieldType.Select => IsCountry(key) || IsCountry(label),
            _ => false
        };
    }

    private static bool IsPhone(string value)
        => value is "phone" or "phonenumber" or "yourphone" or "yourphonenumber" or "telephone" or "telephonenumber" or "mobile" or
            "mobilenumber" or "telefoon" or "telefoonnummer" or "mobiel" or "mobielnummer";

    private static bool IsAddress(string value)
        => value is "address" or "streetaddress" or "youraddress" or "streetandhousenumber" or "adres" or "straat" or "straatenhuisnummer";

    private static bool IsCountry(string value) => value is "country" or "land";

    private static bool IsContact(string value)
        => IsPhone(value) || IsAddress(value) || IsCountry(value) ||
           value is "postalcode" or "postcode" or "zipcode" or "zip" or "city" or "town" or "plaats" or "woonplaats" or "stad";

    private static bool IsEmail(string value)
        => value is "email" or "emailaddress" or "youremail" or "youremailaddress" or "emailadres" or "uwemailadres" or "jouwemailadres";

    private static bool IsName(string value)
        => value is "name" or "fullname" or "yourname" or "yourfullname" or "firstname" or "lastname" or "firstandlastname" or
            "naam" or "volledigenaam" or "voornaam" or "achternaam" or "uwnaam" or "jouwnaam";
}
