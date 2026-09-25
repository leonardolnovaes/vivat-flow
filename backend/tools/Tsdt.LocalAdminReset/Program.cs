using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;

var targetEmail = Environment.GetEnvironmentVariable("LocalAdminReset__Email");

if (string.IsNullOrWhiteSpace(targetEmail))
{
    Console.Error.WriteLine("LocalAdminReset__Email must identify the local administrator to reset.");
    return 1;
}

if (!LocalAdminResetPolicy.AllowsEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), targetEmail))
{
    Console.Error.WriteLine("This utility can run only when ASPNETCORE_ENVIRONMENT is Development.");
    return 1;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__DefaultConnection must be configured.");
    return 1;
}

Console.WriteLine($"This will reset only {targetEmail} in the configured local database.");
Console.Write("Continue? Type RESET to continue: ");
if (!string.Equals(Console.ReadLine(), "RESET", StringComparison.Ordinal))
{
    Console.WriteLine("No changes were made.");
    return 1;
}

var password = ReadPassword("New temporary password: ");
var confirmation = ReadPassword("Confirm new temporary password: ");
if (!string.Equals(password, confirmation, StringComparison.Ordinal))
{
    Console.Error.WriteLine("The password entries did not match. No changes were made.");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

var user = await userManager.FindByEmailAsync(targetEmail);
if (user is null)
{
    Console.Error.WriteLine("The target administrator was not found. No changes were made.");
    return 1;
}

if (!LocalAdminResetPolicy.AllowsTarget(user.IsActive, true))
{
    Console.Error.WriteLine("The target administrator is inactive. No changes were made.");
    return 1;
}

if (!LocalAdminResetPolicy.AllowsTarget(user.IsActive, await userManager.IsInRoleAsync(user, IdentityRoles.Admin)))
{
    Console.Error.WriteLine("The target user is not an ADMIN. No changes were made.");
    return 1;
}

var previousSecurityStamp = user.SecurityStamp;
await using var transaction = await dbContext.Database.BeginTransactionAsync();
var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
var resetResult = await userManager.ResetPasswordAsync(user, resetToken, password);
if (!resetResult.Succeeded)
{
    Console.Error.WriteLine("The password was rejected by the application's Identity policy. No changes were made.");
    return 1;
}

user.MustChangePassword = true;
var updateResult = await userManager.UpdateAsync(user);
if (!updateResult.Succeeded)
{
    Console.Error.WriteLine("The administrator update was not accepted. No changes were made.");
    return 1;
}

var securityStampResult = await userManager.UpdateSecurityStampAsync(user);
if (!securityStampResult.Succeeded || string.Equals(previousSecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
{
    Console.Error.WriteLine("The security stamp was not updated. No changes were made.");
    return 1;
}

await transaction.CommitAsync();

Console.WriteLine("Password reset completed for the configured local administrator.");
Console.WriteLine("ADMIN role preserved; MustChangePassword is enabled; existing sessions were invalidated.");
return 0;

static string ReadPassword(string prompt)
{
    Console.Write(prompt);
    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string([.. characters]);
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Count > 0) characters.RemoveAt(characters.Count - 1);
            continue;
        }

        if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
    }
}
