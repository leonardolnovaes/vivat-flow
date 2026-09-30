using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Tsdt.Api.Audit;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Services;
using Tsdt.Api.Quotes;
using Tsdt.Api.Platform;
using Tsdt.Api.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var localFrontendSameSite = builder.Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (builder.Environment.IsEnvironment("Testing") ? "Host=localhost;Database=testing" : throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured."));
var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}
builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<OrganizationBootstrapOptions>(builder.Configuration.GetSection(OrganizationBootstrapOptions.SectionName));
builder.Services.Configure<PlatformBootstrapAdminOptions>(builder.Configuration.GetSection(PlatformBootstrapAdminOptions.SectionName));
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Password.RequiredLength = PasswordRules.MinimumLength;
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.CommercialAdmin, policy => policy.RequireRole(IdentityRoles.Admin));
    options.AddPolicy(AuthorizationPolicies.PlatformAdministrator, policy => policy.RequireClaim("platform_administrator", "true"));
});
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (frontendOrigins.Length > 0) policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddScoped<IdentityBootstrapper>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, PlatformClaimsPrincipalFactory>();
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

        var organization = user.OrganizationId is Guid organizationId
            ? await context.RequestServices.GetRequiredService<ApplicationDbContext>().Organizations.FindAsync(organizationId)
            : null;
        if (!OrganizationAccess.IsTenantAccessAllowed(user.IsPlatformAdministrator, user.OrganizationId, organization?.Status))
        { context.Response.StatusCode = StatusCodes.Status403Forbidden; await context.Response.WriteAsJsonAsync(new { error = "O acesso da sua organização ao Vivat Flow está bloqueado. Entre em contato com o responsável pela sua conta." }); return; }

        if (user.IsPlatformAdministrator && !context.Request.Path.StartsWithSegments("/api/platform") && !context.Request.Path.StartsWithSegments("/api/auth"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!user.IsPlatformAdministrator && user.OrganizationId is Guid tenantOrganizationId)
        {
            context.Items[TenantContext.OrganizationItemKey] = tenantOrganizationId;
            context.RequestServices.GetRequiredService<ApplicationDbContext>().TenantOrganizationId = tenantOrganizationId;
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
auth.MapGet("/password-policy", (IOptions<IdentityOptions> identityOptions) =>
{
    var password = identityOptions.Value.Password;
    return Results.Ok(new PasswordPolicyResponse(password.RequiredLength, password.RequireDigit, password.RequireLowercase, password.RequireUppercase, password.RequireNonAlphanumeric, PasswordPolicyDescription(password)));
});
auth.MapPost("/login", async (LoginRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.Unauthorized();
    var organization = user.OrganizationId is Guid organizationId ? await dbContext.Organizations.FindAsync(organizationId) : null;
    if (!OrganizationAccess.IsTenantAccessAllowed(user.IsPlatformAdministrator, user.OrganizationId, organization?.Status)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Ok(await CreateCurrentUserResponseAsync(user, userManager, dbContext));
});
auth.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null || !user.IsActive)
    {
        if (user is not null) await signInManager.SignOutAsync();
        return Results.Unauthorized();
    }
    return Results.Ok(await CreateCurrentUserResponseAsync(user, userManager, dbContext));
}).RequireAuthorization();
auth.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IOptions<IdentityOptions> identityOptions) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var user = await userManager.GetUserAsync(context.User);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!result.Succeeded) return IdentityValidationProblem(result, "newPassword", PasswordPolicyDescription(identityOptions.Value.Password));
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
auth.MapPut("/preferred-locale", async (UpdatePreferredLocaleRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
    if (!LocaleRules.IsSupported(request.PreferredLocale)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["preferredLocale"] = ["Unsupported locale."] });
    var user = await userManager.GetUserAsync(context.User);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    user.PreferredLocale = request.PreferredLocale;
    var update = await userManager.UpdateAsync(user);
    if (!update.Succeeded) return IdentityValidationProblem(update);
    return Results.Ok(await CreateCurrentUserResponseAsync(user, userManager, dbContext));
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
    var email = NormalizeEmail(request.Email);
    var validation = ValidateUserInput(fullName, email, role);
    if (validation is not null) return validation;
    if (await userManager.FindByEmailAsync(email!) is not null) return Results.Conflict(new { errors = new Dictionary<string, string[]> { ["email"] = ["Já existe um usuário com este e-mail."] } });

    var temporaryPassword = passwordGenerator.Generate();
    var actor = await userManager.GetUserAsync(context.User);
    if (actor?.OrganizationId is not Guid organizationId || actor.IsPlatformAdministrator) return Results.Conflict(new { error = "Não foi possível determinar a organização do administrador." });
    var user = new ApplicationUser { FullName = fullName!, UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = true, OrganizationId = organizationId };
    await using var transaction = await dbContext.Database.BeginTransactionAsync();
    var creation = await userManager.CreateAsync(user, temporaryPassword);
    if (!creation.Succeeded) return IdentityValidationProblem(creation);
    var assignment = await userManager.AddToRoleAsync(user, role!);
    if (!assignment.Succeeded) return IdentityValidationProblem(assignment);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_CREATED", newRole: role);
    await transaction.CommitAsync();
    return Results.Created($"/api/admin/users/{user.Id}", new CreateUserResponse(await CreateUserAdministrationResponseAsync(user, userManager), temporaryPassword));
});
adminUsers.MapPut("/{id}", async (string id, UpdateUserRequest request, HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) =>
{
    if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." });
    var fullName = request.FullName?.Trim();
    var email = NormalizeEmail(request.Email);
    var validation = ValidateUserInput(fullName, email, IdentityRoles.User, validateRole: false);
    if (validation is not null) return validation;
    var user = await userManager.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    var emailChanged = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
    if (emailChanged && await userManager.FindByEmailAsync(email!) is ApplicationUser existing && existing.Id != user.Id)
        return Results.Conflict(new { errors = new Dictionary<string, string[]> { ["email"] = ["Já existe um usuário com este e-mail."] } });
    user.FullName = fullName!;
    user.Email = email;
    user.UserName = email;
    await using var transaction = await dbContext.Database.BeginTransactionAsync();
    var update = await userManager.UpdateAsync(user);
    if (!update.Succeeded) return IdentityValidationProblem(update);
    if (emailChanged) await userManager.UpdateSecurityStampAsync(user);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_UPDATED");
    await transaction.CommitAsync();
    return Results.Ok(await CreateUserAdministrationResponseAsync(user, userManager));
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
    if (!IsApplicationRole(role)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["O perfil informado não é válido."] });
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
    var clearLockout = await userManager.SetLockoutEndDateAsync(user, null);
    if (!clearLockout.Succeeded) return IdentityValidationProblem(clearLockout);
    var clearFailedAccessCount = await userManager.ResetAccessFailedCountAsync(user);
    if (!clearFailedAccessCount.Succeeded) return IdentityValidationProblem(clearFailedAccessCount);
    user.MustChangePassword = true;
    var update = await userManager.UpdateAsync(user);
    if (!update.Succeeded) return IdentityValidationProblem(update);
    await userManager.UpdateSecurityStampAsync(user);
    await AddAuditAsync(dbContext, await GetActorUserIdAsync(context, userManager), user.Id, "USER_PASSWORD_RESET");
    await transaction.CommitAsync();
    return Results.Ok(new CreateUserResponse(await CreateUserAdministrationResponseAsync(user, userManager), temporaryPassword));
});

app.MapCustomerEndpoints();
app.MapServiceEndpoints();
app.MapQuoteEndpoints();
app.MapContractEndpoints();
app.MapPlatformOrganizationEndpoints();
app.MapServiceLineEndpoints();

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

static async Task<CurrentUserResponse> CreateCurrentUserResponseAsync(ApplicationUser user, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext)
{
    var roles = await userManager.GetRolesAsync(user);
    var organization = !user.IsPlatformAdministrator && user.OrganizationId is Guid organizationId
        ? await dbContext.Organizations.AsNoTracking().Where(item => item.Id == organizationId).Select(item => new CurrentOrganizationResponse(item.Id, item.Name)).SingleOrDefaultAsync()
        : null;
    return new CurrentUserResponse(user.Id, user.FullName, user.Email!, roles.ToArray(), user.MustChangePassword, user.IsPlatformAdministrator, user.PreferredLocale, organization);
}

static async Task<UserAdministrationResponse> CreateUserAdministrationResponseAsync(ApplicationUser user, UserManager<ApplicationUser> userManager) => new(user.Id, user.FullName, user.Email!, (await userManager.GetRolesAsync(user)).ToArray(), user.IsActive, user.MustChangePassword);
static bool IsApplicationRole(string? role) => role is IdentityRoles.Admin or IdentityRoles.Manager or IdentityRoles.User;
static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
{
    try { await antiforgery.ValidateRequestAsync(context); return true; }
    catch (AntiforgeryValidationException) { return false; }
}
static IResult? ValidateUserInput(string? fullName, string? email, string? role, bool validateRole = true)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(fullName)) errors["fullName"] = ["Informe o nome completo."];
    else if (fullName.Length > 120) errors["fullName"] = ["O nome completo deve ter no máximo 120 caracteres."];
    if (string.IsNullOrWhiteSpace(email)) errors["email"] = ["Informe o e-mail."];
    else if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _)) errors["email"] = ["Informe um e-mail válido com no máximo 254 caracteres."];
    if (validateRole && !IsApplicationRole(role)) errors["role"] = ["Selecione um perfil válido."];
    return errors.Count == 0 ? null : Results.ValidationProblem(errors);
}
static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
static string PasswordPolicyDescription(PasswordOptions password)
{
    var requirements = new List<string> { $"pelo menos {password.RequiredLength} caracteres" };
    if (password.RequireUppercase) requirements.Add("letra maiúscula");
    if (password.RequireLowercase) requirements.Add("letra minúscula");
    if (password.RequireDigit) requirements.Add("número");
    if (password.RequireNonAlphanumeric) requirements.Add("símbolo");
    return $"A senha deve ter {string.Join(", ", requirements)}.";
}
static IResult IdentityValidationProblem(IdentityResult result, string fallbackField = "identity", string? passwordDescription = null)
{
    var errors = result.Errors.Select(error => error.Code switch
    {
        "PasswordTooShort" => passwordDescription ?? "A senha não atende aos requisitos.",
        "PasswordRequiresNonAlphanumeric" => passwordDescription ?? "A senha não atende aos requisitos.",
        "PasswordRequiresDigit" => passwordDescription ?? "A senha não atende aos requisitos.",
        "PasswordRequiresLower" => passwordDescription ?? "A senha não atende aos requisitos.",
        "PasswordRequiresUpper" => passwordDescription ?? "A senha não atende aos requisitos.",
        _ => "Não foi possível concluir a solicitação."
    }).Distinct().ToArray();
    return Results.ValidationProblem(new Dictionary<string, string[]> { [fallbackField] = errors });
}
static async Task<string> GetActorUserIdAsync(HttpContext context, UserManager<ApplicationUser> userManager) => (await userManager.GetUserAsync(context.User))?.Id ?? throw new UnauthorizedAccessException();
static async Task<bool> IsLastActiveAdminAsync(ApplicationDbContext dbContext) => await dbContext.UserRoles.Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name }).Join(dbContext.Users, item => item.UserId, user => user.Id, (item, user) => new { item.Name, user.IsActive }).CountAsync(item => item.IsActive && item.Name == IdentityRoles.Admin) == 1;
static async Task AddAuditAsync(ApplicationDbContext dbContext, string actorUserId, string targetUserId, string action, string? oldRole = null, string? newRole = null)
{
    dbContext.UserAdministrationAuditRecords.Add(new UserAdministrationAuditRecord { ActorUserId = actorUserId, TargetUserId = targetUserId, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, OldRole = oldRole, NewRole = newRole });
    await dbContext.SaveChangesAsync();
}

public partial class Program;
