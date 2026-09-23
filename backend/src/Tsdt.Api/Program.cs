using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Audit;
using Tsdt.Api.Identity;

var builder = WebApplication.CreateBuilder(args);
var localFrontendSameSite = builder.Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (builder.Environment.IsEnvironment("Testing") ? "Host=localhost;Database=testing" : throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured."));
var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}
builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
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
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-tsdt-auth";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = localFrontendSameSite;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-tsdt-csrf";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = localFrontendSameSite;
});
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (frontendOrigins.Length > 0) policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddScoped<IdentityBootstrapper>();
builder.Services.AddSingleton<TemporaryPasswordGenerator>();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        context.Request.Path.StartsWithSegments("/api"))
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || !user.IsActive)
        {
            await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (user.MustChangePassword &&
            !context.Request.Path.StartsWithSegments("/api/auth/me") &&
            !context.Request.Path.StartsWithSegments("/api/auth/change-password") &&
            !context.Request.Path.StartsWithSegments("/api/auth/logout") &&
            !context.Request.Path.StartsWithSegments("/api/auth/csrf"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

var auth = app.MapGroup("/api/auth");
auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new CsrfTokenResponse(tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery did not issue a request token.")));
});
auth.MapPost("/login", async (LoginRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.Unauthorized();
    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Ok(await CreateCurrentUserResponseAsync(user, userManager));
});
auth.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null || !user.IsActive)
    {
        if (user is not null) await signInManager.SignOutAsync();
        return Results.Unauthorized();
    }
    return Results.Ok(await CreateCurrentUserResponseAsync(user, userManager));
}).RequireAuthorization();
auth.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var user = await userManager.GetUserAsync(context.User);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!result.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["The password change request was not accepted."] });
    user.MustChangePassword = false;
    await userManager.UpdateAsync(user);
    await signInManager.RefreshSignInAsync(user);
    return Results.NoContent();
}).RequireAuthorization();
auth.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<ApplicationUser> signInManager) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

var adminUsers = app.MapGroup("/api/admin/users").RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin));
adminUsers.MapGet("", async (string? search, bool? isActive, ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager) =>
{
    var users = dbContext.Users.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(search))
    {
        var normalizedSearch = search.Trim().ToUpper();
        users = users.Where(user => user.FullName.ToUpper().Contains(normalizedSearch) || user.Email!.ToUpper().Contains(normalizedSearch));
    }
    if (isActive.HasValue) users = users.Where(user => user.IsActive == isActive.Value);

    var result = new List<UserAdministrationResponse>();
    foreach (var user in await users.OrderBy(user => user.FullName).ToListAsync()) result.Add(await CreateUserAdministrationResponseAsync(user, userManager));
    return Results.Ok(result);
});
adminUsers.MapGet("/{id}", async (string id, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    return user is null ? Results.NotFound() : Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
});
adminUsers.MapPost("", async (CreateUserRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext, TemporaryPasswordGenerator passwordGenerator) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    var fullName = request.FullName?.Trim();
    var role = request.Role?.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(fullName)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["fullName"] = ["Full name is required."] });
    if (!IsApplicationRole(role)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["The role is not valid."] });
    var email = request.Email?.Trim();
    if (string.IsNullOrWhiteSpace(email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Email is required."] });
    if (await userManager.FindByEmailAsync(email) is not null) return Results.Conflict(new { error = "An account with this email already exists." });

    var temporaryPassword = passwordGenerator.Generate();
    var user = new ApplicationUser { FullName = fullName, UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = true };
    await using var transaction = await dbContext.Database.BeginTransactionAsync();
    var creation = await userManager.CreateAsync(user, temporaryPassword);
    if (!creation.Succeeded) return IdentityValidationProblem(creation);
    var assignment = await userManager.AddToRoleAsync(user, role!);
    if (!assignment.Succeeded) return IdentityValidationProblem(assignment);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_CREATED", newRole: role);
    await transaction.CommitAsync();
    return Results.Created($"/api/admin/users/{user.Id}", new CreateUserResponse(await CreateUserAdministrationResponseAsync(user, userManager), temporaryPassword));
});
adminUsers.MapPost("/{id}/activate", async (string id, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    if (!user.IsActive)
    {
        user.IsActive = true;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return IdentityValidationProblem(update);
        await userManager.UpdateSecurityStampAsync(user);
        await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_ACTIVATED");
        await transaction.CommitAsync();
    }
    return Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
});
adminUsers.MapPost("/{id}/deactivate", async (string id, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    var actorId = await GetActorUserIdAsync(context, userManager);
    if (actorId == id) return Results.Conflict(new { error = "Administrators cannot deactivate their own account." });
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    if (user.IsActive && await userManager.IsInRoleAsync(user, IdentityRoles.Admin) && await IsLastActiveAdminAsync(dbContext)) return Results.Conflict(new { error = "The last active administrator cannot be deactivated." });
    if (user.IsActive)
    {
        user.IsActive = false;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return IdentityValidationProblem(update);
        await userManager.UpdateSecurityStampAsync(user);
        await AddAuditAsync(dbContext, actorId, user.Id, "USER_DEACTIVATED");
    }
    await transaction.CommitAsync();
    return Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
});
adminUsers.MapPut("/{id}/role", async (string id, ChangeUserRoleRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    var role = request.Role?.Trim().ToUpperInvariant();
    if (!IsApplicationRole(role)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["The role is not valid."] });
    await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    var oldRoles = await userManager.GetRolesAsync(user);
    var oldRole = oldRoles.SingleOrDefault();
    if (oldRole == role) return Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
    if (user.IsActive && oldRole == IdentityRoles.Admin && role != IdentityRoles.Admin && await IsLastActiveAdminAsync(dbContext)) return Results.Conflict(new { error = "The last active administrator must remain an administrator." });
    var removal = await userManager.RemoveFromRolesAsync(user, oldRoles);
    if (!removal.Succeeded) return IdentityValidationProblem(removal);
    var assignment = await userManager.AddToRoleAsync(user, role!);
    if (!assignment.Succeeded) return IdentityValidationProblem(assignment);
    await userManager.UpdateSecurityStampAsync(user);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_ROLE_CHANGED", oldRole, role);
    await transaction.CommitAsync();
    return Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
});
adminUsers.MapPost("/{id}/reset-password", async (string id, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext, TemporaryPasswordGenerator passwordGenerator) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    if (!user.IsActive) return Results.Conflict(new { error = "Inactive users cannot have their password reset." });
    var temporaryPassword = passwordGenerator.Generate();
    await using var transaction = await dbContext.Database.BeginTransactionAsync();
    var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
    var reset = await userManager.ResetPasswordAsync(user, resetToken, temporaryPassword);
    if (!reset.Succeeded) return IdentityValidationProblem(reset);
    user.MustChangePassword = true;
    var update = await userManager.UpdateAsync(user);
    if (!update.Succeeded) return IdentityValidationProblem(update);
    await userManager.UpdateSecurityStampAsync(user);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_PASSWORD_RESET");
    await transaction.CommitAsync();
    return Results.Ok(new CreateUserResponse(await CreateUserAdministrationResponseAsync(user, userManager), temporaryPassword));
});

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (app.Environment.IsEnvironment("Testing"))
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
    else
    {
        await dbContext.Database.MigrateAsync();
    }
    await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().InitializeAsync();
}
app.Run();

static async Task<CurrentUserResponse> CreateCurrentUserResponseAsync(ApplicationUser user, UserManager<ApplicationUser> userManager)
{
    var roles = await userManager.GetRolesAsync(user);
    return new CurrentUserResponse(user.Id, user.FullName, user.Email!, roles.ToArray(), user.MustChangePassword);
}

static async Task<UserAdministrationResponse> CreateUserAdministrationResponseAsync(ApplicationUser user, UserManager<ApplicationUser> userManager) => new(user.Id, user.FullName, user.Email!, (await userManager.GetRolesAsync(user)).ToArray(), user.IsActive, user.MustChangePassword);
static bool IsApplicationRole(string? role) => role is IdentityRoles.Admin or IdentityRoles.Manager or IdentityRoles.User;
static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
{
    try { await antiforgery.ValidateRequestAsync(context); return true; }
    catch (AntiforgeryValidationException) { return false; }
}
static IResult IdentityValidationProblem(IdentityResult result) => Results.ValidationProblem(new Dictionary<string, string[]> { ["identity"] = ["The request was not accepted."] });
static async Task<string> GetActorUserIdAsync(HttpContext context, UserManager<ApplicationUser> userManager) => (await userManager.GetUserAsync(context.User))?.Id ?? throw new UnauthorizedAccessException();
static async Task<bool> IsLastActiveAdminAsync(ApplicationDbContext dbContext) => await dbContext.UserRoles.Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name }).Join(dbContext.Users, item => item.UserId, user => user.Id, (item, user) => new { item.Name, user.IsActive }).CountAsync(item => item.IsActive && item.Name == IdentityRoles.Admin) == 1;
static async Task AddAuditAsync(ApplicationDbContext dbContext, string actorUserId, string targetUserId, string action, string? oldRole = null, string? newRole = null)
{
    dbContext.UserAdministrationAuditRecords.Add(new UserAdministrationAuditRecord { ActorUserId = actorUserId, TargetUserId = targetUserId, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, OldRole = oldRole, NewRole = newRole });
    await dbContext.SaveChangesAsync();
}

public partial class Program;
