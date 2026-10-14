using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public interface IApplicationDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class ApplicationDatabaseInitializer : IApplicationDatabaseInitializer
{
    private static readonly string[] DefaultRoles = ["SuperAdmin", "Admin", "Teacher", "Student"];

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApplicationDatabaseInitializer> _logger;

    public ApplicationDatabaseInitializer(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration,
        ILogger<ApplicationDatabaseInitializer> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.MigrateAsync(cancellationToken);
        await EnsureRolesAsync();
        await EnsureConfiguredUsersAsync();
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var roleName in DefaultRoles)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create the {roleName} role: {FormatErrors(result)}");
            }
        }
    }

    private async Task EnsureConfiguredUsersAsync()
    {
        await EnsureUserAsync(
            configurationPrefix: "Admin",
            roleName: "Admin",
            defaultFirstName: "Admin",
            defaultLastName: "User");

        await EnsureUserAsync(
            configurationPrefix: "SuperAdmin",
            roleName: "SuperAdmin",
            defaultFirstName: "Super",
            defaultLastName: "Admin");
    }

    private async Task EnsureUserAsync(
        string configurationPrefix,
        string roleName,
        string defaultFirstName,
        string defaultLastName)
    {
        var email = _configuration[$"{configurationPrefix}:Email"];
        var password = _configuration[$"{configurationPrefix}:Password"];

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "{UserType} seeding skipped because both email and password must be configured.",
                configurationPrefix);
            return;
        }

        var firstName = _configuration[$"{configurationPrefix}:FirstName"] ?? defaultFirstName;
        var lastName = _configuration[$"{configurationPrefix}:LastName"] ?? defaultLastName;
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                _logger.LogWarning(
                    "{UserType} user creation failed. Identity errors: {IdentityErrors}",
                    configurationPrefix,
                    FormatErrors(createResult));
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
        {
            user.FirstName ??= firstName;
            user.LastName ??= lastName;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _logger.LogWarning(
                    "{UserType} user profile update failed. Identity errors: {IdentityErrors}",
                    configurationPrefix,
                    FormatErrors(updateResult));
            }
        }

        if (!await _userManager.IsInRoleAsync(user, roleName))
        {
            var roleResult = await _userManager.AddToRoleAsync(user, roleName);
            if (!roleResult.Succeeded)
            {
                _logger.LogWarning(
                    "{UserType} role assignment failed. Identity errors: {IdentityErrors}",
                    configurationPrefix,
                    FormatErrors(roleResult));
            }
        }
    }

    private static string FormatErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(error => error.Description));
}
