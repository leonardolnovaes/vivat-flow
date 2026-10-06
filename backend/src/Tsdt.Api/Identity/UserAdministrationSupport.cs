using Microsoft.AspNetCore.Identity;

namespace Tsdt.Api.Identity;

public static class UserAdministrationSupport
{
    public static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static IResult? ValidateInput(string? fullName, string? email, string? role, bool validateRole = true)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fullName)) errors["fullName"] = ["Informe o nome completo."];
        else if (fullName.Length > 120) errors["fullName"] = ["O nome completo deve ter no máximo 120 caracteres."];
        if (string.IsNullOrWhiteSpace(email)) errors["email"] = ["Informe o e-mail."];
        else if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _)) errors["email"] = ["Informe um e-mail válido com no máximo 254 caracteres."];
        if (validateRole && !IsApplicationRole(role)) errors["role"] = ["Selecione um perfil válido."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    public static bool IsApplicationRole(string? role) => role is IdentityRoles.Admin or IdentityRoles.Manager or IdentityRoles.User;

    public static async Task<UserAdministrationResponse> CreateResponseAsync(ApplicationUser user, UserManager<ApplicationUser> userManager) =>
        new(user.Id, user.FullName, user.Email!, (await userManager.GetRolesAsync(user)).ToArray(), user.IsActive, user.MustChangePassword);

    public static IResult ValidationProblem(IdentityResult result, string fallbackField = "identity", string? passwordDescription = null)
    {
        var errors = result.Errors.Select(error => error.Code switch
        {
            "PasswordTooShort" or "PasswordRequiresNonAlphanumeric" or "PasswordRequiresDigit" or "PasswordRequiresLower" or "PasswordRequiresUpper" => passwordDescription ?? "A senha não atende aos requisitos.",
            _ => "Não foi possível concluir a solicitação."
        }).Distinct().ToArray();
        return Results.ValidationProblem(new Dictionary<string, string[]> { [fallbackField] = errors });
    }
}
