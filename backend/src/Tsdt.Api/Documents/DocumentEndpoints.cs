using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Documents;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var customers = app.MapGroup("/api/customers/{customerId:guid}/documents")
            .RequireAuthorization(AuthorizationPolicies.WorkOrderExecution);
        customers.MapGet("", ListAsync);
        customers.MapPost("", UploadAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        app.MapGet("/api/documents/{id:guid}/download", DownloadAsync)
            .RequireAuthorization(AuthorizationPolicies.WorkOrderExecution);
    }

    private static async Task<IResult> UploadAsync(Guid customerId, HttpContext context, IAntiforgery antiforgery, DocumentService documents)
    {
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." }); }
        if (!context.Request.HasFormContentType) return Error("file", "Envie um arquivo em formulário multipart.");
        try
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || form.Files.Count != 1) return Error("file", "Selecione um arquivo para enviar.");
            if (!Enum.TryParse<DocumentCategory>(form["category"].ToString(), true, out var category) || !Enum.IsDefined(category))
                return Error("category", "Selecione uma categoria válida.");
            var contextTypeText = form["contextType"].ToString();
            var contextIdText = form["contextId"].ToString();
            DocumentContextType? contextType = null;
            Guid? contextId = null;
            if (!string.IsNullOrWhiteSpace(contextTypeText))
            {
                if (!Enum.TryParse<DocumentContextType>(contextTypeText, true, out var parsed) || !Enum.IsDefined(parsed))
                    return Error("contextType", "Selecione um contexto válido.");
                contextType = parsed;
            }
            if (!string.IsNullOrWhiteSpace(contextIdText))
            {
                if (!Guid.TryParse(contextIdText, out var parsed) || parsed == Guid.Empty) return Error("contextId", "Informe uma referência válida.");
                contextId = parsed;
            }
            await using var stream = file.OpenReadStream();
            var upload = new DocumentUpload(customerId, category, form["description"].ToString(), contextType, contextId,
                file.FileName, file.ContentType, file.Length, stream);
            var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
            var result = await documents.UploadAsync(TenantContext.OrganizationId(context), actor, upload, context.RequestAborted);
            return Results.Created($"/api/documents/{result.Id}/download", result);
        }
        catch (DocumentValidationException error) { return Error(error.Field, error.Message); }
        catch (DocumentNotFoundException) { return Results.NotFound(); }
        catch (InvalidDataException) { return Error("file", "O arquivo enviado é inválido ou excede o limite permitido."); }
        catch (BadHttpRequestException) { return Error("file", "O arquivo enviado é inválido ou excede o limite permitido."); }
        catch { return Results.Problem("Não foi possível salvar o documento. Tente novamente.", statusCode: 500); }
    }

    private static async Task<IResult> ListAsync(Guid customerId, int? page, int? pageSize, HttpContext context, DocumentService documents)
    {
        try { return Results.Ok(await documents.ListAsync(TenantContext.OrganizationId(context), customerId, page ?? 1, pageSize ?? 25, context.RequestAborted)); }
        catch (DocumentNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> DownloadAsync(Guid id, HttpContext context, DocumentService documents)
    {
        try
        {
            var download = await documents.DownloadAsync(TenantContext.OrganizationId(context), id, context.RequestAborted);
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(download.Content, download.ContentType, download.FileName);
        }
        catch (DocumentNotFoundException) { return Results.NotFound(); }
        catch { return Results.Problem("Não foi possível baixar o documento. Tente novamente.", statusCode: 500); }
    }

    private static IResult Error(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
