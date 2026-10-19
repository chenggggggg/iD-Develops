using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace iD_Develops.E2ETests;

internal sealed class CatalogBrowsingFixture(ApplicationDbContext db) : IAsyncDisposable
{
    public static string PublicBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PUBLIC_BASE_URL") ?? "http://id.localhost:5000").TrimEnd('/');
    public static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    public CatalogProduct Credit { get; private set; } = null!;
    public CatalogProduct Standard { get; private set; } = null!;
    public CatalogProduct Draft { get; private set; } = null!;
    public CatalogProduct Hidden { get; private set; } = null!;
    public ApplicationUser User { get; private set; } = null!;
    public string Password { get; } = $"Catalog!{Guid.NewGuid():N}aA1";

    public static async Task<CatalogBrowsingFixture> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set IDDEVELOPS_E2E_DATABASE_CONNECTION to the running local application's PostgreSQL connection string.");
        if (!IsLocalHost(new Uri(PublicBaseUrl).Host) || !IsLocalHost(new Uri(PortalBaseUrl).Host) ||
            !IsLocalHost(new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty))
            throw new InvalidOperationException("Catalog browsing fixtures require local application and database hosts.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var fixture = new CatalogBrowsingFixture(new ApplicationDbContext(options));
        try
        {
            await fixture.SeedAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        Credit = Product("Credits", CatalogProductType.Credit);
        Standard = Product("Standard", CatalogProductType.Standard);
        Draft = Product("Draft", CatalogProductType.Credit);
        Draft.Status = CatalogProductStatus.Draft;
        Hidden = Product("Hidden", CatalogProductType.Credit);
        Hidden.HideFromProductsPage = true;
        Credit.FormFields =
        [
            Field("customer_name", "Your name", CatalogFormFieldType.Text),
            Field("customer_email", "Your email", CatalogFormFieldType.Email, required: true),
            Field("telephone", "Telephone", CatalogFormFieldType.Tel),
            Field("message", "Your message", CatalogFormFieldType.Textarea),
            Field("format", "Preferred format", CatalogFormFieldType.Select, options: "Online\nIn person"),
            Field("topics", "Topics", CatalogFormFieldType.Checkbox, options: "plain\tConversation\ntext\tOther topic\t100"),
            Field("choice", "Choose one", CatalogFormFieldType.Checkbox, options: "plain\tMorning\nplain\tEvening"),
            Field("items", "Learning goals", CatalogFormFieldType.NumberedList),
            Field("note", "Additional information", CatalogFormFieldType.Note)
        ];
        Credit.FormFields.Single(field => field.Key == "choice").AllowMultipleOptions = false;
        db.CatalogProducts.AddRange(Credit, Standard, Draft, Hidden);

        var email = $"catalog-{suffix}@example.test";
        User = new ApplicationUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Catalog",
            LastName = "Learner",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        User.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(User, Password);
        db.Users.Add(User);
        var studentRole = await db.Roles.SingleAsync(role => role.NormalizedName == "STUDENT");
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = User.Id, RoleId = studentRole.Id });
        await db.SaveChangesAsync();

        CatalogProduct Product(string name, CatalogProductType type) => new()
        {
            Name = $"Catalog {name} {suffix}",
            Slug = $"e2e-catalog-{name.ToLowerInvariant()}-{suffix}",
            ProductType = type,
            WorkflowType = CatalogWorkflowType.FormSubmission,
            Status = CatalogProductStatus.Published,
            BasePrice = 25m
        };
    }

    private static CatalogProductFormField Field(string key, string label, CatalogFormFieldType type,
        bool required = false, string? options = null) => new()
        {
            Key = key,
            Label = label,
            FieldType = type,
            IsRequired = required,
            OptionsText = options
        };

    private static bool IsLocalHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
           (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));

    public async ValueTask DisposeAsync()
    {
        try
        {
            var ids = new[] { Credit?.Id, Standard?.Id, Draft?.Id, Hidden?.Id }
                .Where(id => id.HasValue && id.Value > 0).Select(id => id!.Value).ToArray();
            await db.CatalogProducts.Where(product => ids.Contains(product.Id)).ExecuteDeleteAsync();
            if (User != null)
                await db.Users.Where(user => user.Id == User.Id).ExecuteDeleteAsync();
        }
        finally
        {
            await db.DisposeAsync();
        }
    }
}
