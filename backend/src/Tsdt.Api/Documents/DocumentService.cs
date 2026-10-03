using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Documents;

public sealed record DocumentUpload(Guid CustomerId, DocumentCategory Category, DocumentPurpose Purpose, string? Description,
    DocumentContextType? ContextType, Guid? ContextId, string FileName, string? ContentType,
    long DeclaredLength, Stream Content);

public sealed class DocumentService(ApplicationDbContext db, IDocumentStorage storage, IOptions<DocumentStorageOptions> options)
{
    public async Task<DocumentResponse> UploadAsync(Guid organizationId, string actor, DocumentUpload upload, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(upload.Category)) throw new DocumentValidationException("category", "Selecione uma categoria válida.");
        if (!Enum.IsDefined(upload.Purpose)) throw new DocumentValidationException("purpose", "Selecione uma finalidade válida.");
        if (upload.ContextType.HasValue != upload.ContextId.HasValue ||
            upload.ContextType is { } contextType && !Enum.IsDefined(contextType))
            throw new DocumentValidationException("contextType", "Informe um contexto válido ou deixe os dois campos vazios.");
        if (upload.Description?.Trim().Length > 1000) throw new DocumentValidationException("description", "Use no máximo 1.000 caracteres na descrição.");
        if (upload.DeclaredLength <= 0) throw new DocumentValidationException("file", "Selecione um arquivo não vazio.");
        var limit = options.Value.MaxFileSizeBytes;
        DocumentStorageOptions.ValidateMaxFileSize(limit);
        if (upload.DeclaredLength > limit) throw new DocumentValidationException("file", "O arquivo excede o limite de tamanho permitido.");
        var fileName = DocumentFilePolicy.SanitizeFileName(upload.FileName);

        if (!await db.Customers.AnyAsync(customer => customer.Id == upload.CustomerId && customer.OrganizationId == organizationId, cancellationToken) ||
            !await ContextBelongsToCustomerAsync(organizationId, upload.CustomerId, upload.ContextType, upload.ContextId, cancellationToken))
            throw new DocumentNotFoundException();

        await using var content = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await upload.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (content.Length + read > limit) throw new DocumentValidationException("file", "O arquivo excede o limite de tamanho permitido.");
            await content.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (content.Length == 0) throw new DocumentValidationException("file", "Selecione um arquivo não vazio.");
        var contentType = DocumentFilePolicy.ValidatedContentType(fileName, upload.ContentType, content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, 12)));
        content.Position = 0;

        var now = DateTimeOffset.UtcNow;
        var document = new DocumentRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, CustomerId = upload.CustomerId,
            OriginalFileName = fileName, StorageKey = Guid.NewGuid(), ContentType = contentType, SizeBytes = content.Length,
            Category = upload.Category, Purpose = upload.Purpose, Description = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description.Trim(),
            ContextType = upload.ContextType, ContextId = upload.ContextId, UploadedAtUtc = now, UploadedByUserId = actor };
        var stored = false;
        try
        {
            await storage.SaveAsync(organizationId, document.StorageKey, content, cancellationToken);
            stored = true;
            db.Documents.Add(document);
            db.DocumentAuditRecords.Add(new DocumentAuditRecord { Id = Guid.NewGuid(), DocumentId = document.Id,
                OrganizationId = organizationId, CustomerId = document.CustomerId, ActorUserId = actor,
                Action = "DOCUMENT_UPLOADED", OccurredAtUtc = now, ContextType = document.ContextType, ContextId = document.ContextId });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (stored) await storage.DeleteAsync(organizationId, document.StorageKey, CancellationToken.None);
            throw;
        }
        return Response(document);
    }

    public async Task<DocumentListResponse> ListAsync(Guid organizationId, Guid customerId, string actor, DocumentAccessLevel access, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!await db.Customers.AnyAsync(customer => customer.Id == customerId && customer.OrganizationId == organizationId &&
            (access != DocumentAccessLevel.User || customer.IsActive && db.WorkOrders.Any(order => order.OrganizationId == organizationId && order.CustomerId == customerId && order.AssignedUserId == actor)), cancellationToken))
            throw new DocumentNotFoundException();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Documents.AsNoTracking().Where(document => document.OrganizationId == organizationId && document.CustomerId == customerId);
        query = VisibleToReader(query, organizationId, actor, access);
        var total = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) return new DocumentListResponse([], page, pageSize, total);
        var documents = db.Database.ProviderName?.Contains("Sqlite") == true
            ? (await query.ToListAsync(cancellationToken)).OrderByDescending(document => document.UploadedAtUtc).ThenByDescending(document => document.Id).Skip((int)offset).Take(pageSize).ToList()
            : await query.OrderByDescending(document => document.UploadedAtUtc).ThenByDescending(document => document.Id).Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);
        var labels = await ContextLabelsAsync(organizationId, customerId, documents
            .Where(document => document.ContextType.HasValue && document.ContextId.HasValue)
            .Select(document => (document.ContextType!.Value, document.ContextId!.Value)).ToList(), cancellationToken);
        var userIds = documents.Select(document => document.UploadedByUserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(user => user.OrganizationId == organizationId && userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName }).ToDictionaryAsync(user => user.Id, user => user.FullName, cancellationToken);
        return new DocumentListResponse(documents.Select(document => Response(document) with
        {
            ContextLabel = document.ContextType is null ? "Cliente" :
                labels.GetValueOrDefault((document.ContextType.Value, document.ContextId!.Value), "Contexto indisponível"),
            UploadedByName = users.GetValueOrDefault(document.UploadedByUserId, "Usuário indisponível")
        }).ToList(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<DocumentContextOption>> ContextOptionsAsync(Guid organizationId, Guid customerId,
        DocumentAccessLevel access, CancellationToken cancellationToken = default)
    {
        if (access == DocumentAccessLevel.User) throw new DocumentNotFoundException();
        if (!await db.Customers.AnyAsync(customer => customer.Id == customerId && customer.OrganizationId == organizationId, cancellationToken))
            throw new DocumentNotFoundException();
        var options = new List<DocumentContextOption> { new(DocumentContextType.Customer, customerId, "Cliente") };
        options.AddRange(await UnitOptionsAsync(organizationId, customerId, cancellationToken));
        options.AddRange(await db.WorkOrders.AsNoTracking().Where(order => order.CustomerId == customerId && order.OrganizationId == organizationId)
            .OrderByDescending(order => order.Number).Select(order => new DocumentContextOption(DocumentContextType.WorkOrder, order.Id, "OS " + order.Number)).ToListAsync(cancellationToken));
        if (access == DocumentAccessLevel.Admin)
        {
            options.AddRange(await db.Quotes.AsNoTracking().Where(quote => quote.CustomerId == customerId && quote.OrganizationId == organizationId)
                .OrderByDescending(quote => quote.Number).Select(quote => new DocumentContextOption(DocumentContextType.Quote, quote.Id, "Orçamento " + quote.Number)).ToListAsync(cancellationToken));
            options.AddRange(await ContractOptionsAsync(organizationId, customerId, cancellationToken));
        }
        return options;
    }

    private async Task<Dictionary<(DocumentContextType, Guid), string>> ContextLabelsAsync(Guid organizationId, Guid customerId,
        IReadOnlyList<(DocumentContextType Type, Guid Id)> contexts, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(DocumentContextType, Guid), string>();
        var ids = contexts.Select(context => context.Id).Distinct().ToList();
        if (ids.Count == 0) return labels;
        foreach (var id in contexts.Where(context => context.Type == DocumentContextType.Customer && context.Id == customerId).Select(context => context.Id))
            labels[(DocumentContextType.Customer, id)] = "Cliente";
        if (contexts.Any(context => context.Type == DocumentContextType.CustomerUnit))
        {
            var units = await UnitOptionsAsync(organizationId, customerId, cancellationToken);
            foreach (var unit in units.Where(unit => ids.Contains(unit.Id)))
                labels[(DocumentContextType.CustomerUnit, unit.Id)] = unit.Label;
        }
        var orders = await db.WorkOrders.AsNoTracking().Where(order => ids.Contains(order.Id) && order.CustomerId == customerId && order.OrganizationId == organizationId)
            .Select(order => new { order.Id, order.Number }).ToListAsync(cancellationToken);
        foreach (var order in orders) labels[(DocumentContextType.WorkOrder, order.Id)] = "OS " + order.Number;
        if (contexts.Any(context => context.Type == DocumentContextType.Quote))
        {
            var quotes = await db.Quotes.AsNoTracking().Where(quote => ids.Contains(quote.Id) && quote.CustomerId == customerId && quote.OrganizationId == organizationId)
                .Select(quote => new { quote.Id, quote.Number }).ToListAsync(cancellationToken);
            foreach (var quote in quotes) labels[(DocumentContextType.Quote, quote.Id)] = "Orçamento " + quote.Number;
        }
        if (contexts.Any(context => context.Type == DocumentContextType.Contract))
        {
            var contracts = await ContractOptionsAsync(organizationId, customerId, cancellationToken);
            foreach (var contract in contracts.Where(contract => ids.Contains(contract.Id)))
                labels[(DocumentContextType.Contract, contract.Id)] = contract.Label;
        }
        return labels;
    }

    private async Task<IReadOnlyList<DocumentContextOption>> UnitOptionsAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken)
    {
        var units = await db.CustomerUnits.AsNoTracking()
            .Where(unit => unit.CustomerId == customerId && unit.Customer.OrganizationId == organizationId)
            .Select(unit => new { unit.Id, unit.Name, unit.Street, unit.Number, unit.Complement, unit.City, unit.StateCode, unit.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var descriptions = units.Select(unit => new
        {
            unit.Id, unit.Name, unit.CreatedAtUtc,
            Label = $"{unit.Name} — {unit.Street}, {unit.Number}{(string.IsNullOrWhiteSpace(unit.Complement) ? "" : ", " + unit.Complement)} — {unit.City}/{unit.StateCode}"
        });
        return descriptions.GroupBy(unit => unit.Label, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderBy(unit => unit.CreatedAtUtc).ThenBy(unit => unit.Id)
                .Select((unit, index) => new DocumentContextOption(DocumentContextType.CustomerUnit, unit.Id,
                    group.Count() == 1 ? unit.Label : $"{unit.Label} (unidade {index + 1})")))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ThenBy(option => option.Id)
            .ToList();
    }

    private async Task<IReadOnlyList<DocumentContextOption>> ContractOptionsAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken)
    {
        var contracts = await (from contract in db.Contracts.AsNoTracking()
            join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
            where contract.CustomerId == customerId && contract.OrganizationId == organizationId &&
                quote.CustomerId == customerId && quote.OrganizationId == organizationId
            select new { contract.Id, contract.QuoteId, contract.CreatedAtUtc, quote.Number }).ToListAsync(cancellationToken);
        return contracts.GroupBy(contract => contract.QuoteId)
            .SelectMany(group => group.OrderBy(contract => contract.CreatedAtUtc).ThenBy(contract => contract.Id)
                .Select((contract, index) => new { contract.Id, contract.Number, Sequence = index + 1 }))
            .OrderByDescending(contract => contract.Number).ThenByDescending(contract => contract.Sequence)
            .Select(contract => new DocumentContextOption(DocumentContextType.Contract, contract.Id,
                $"Contrato {contract.Sequence} do orçamento {contract.Number}"))
            .ToList();
    }

    public async Task<DocumentDownload> DownloadAsync(Guid organizationId, Guid documentId, string actor, DocumentAccessLevel access, CancellationToken cancellationToken = default)
    {
        var query = db.Documents.AsNoTracking().Where(item => item.Id == documentId && item.OrganizationId == organizationId);
        query = VisibleToReader(query, organizationId, actor, access);
        var document = await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new DocumentNotFoundException();
        var content = await storage.OpenReadAsync(organizationId, document.StorageKey, cancellationToken);
        return new DocumentDownload(content, document.ContentType, document.OriginalFileName);
    }

    private IQueryable<DocumentRecord> VisibleToReader(IQueryable<DocumentRecord> query, Guid organizationId, string actor, DocumentAccessLevel access)
    {
        if (access == DocumentAccessLevel.Admin) return query;
        query = query.Where(document => document.ContextType != DocumentContextType.Quote &&
            document.ContextType != DocumentContextType.Contract &&
            document.Category != DocumentCategory.Contract && document.Category != DocumentCategory.SignedDocument);
        if (access == DocumentAccessLevel.Manager) return query;
        return query.Where(document => document.ContextType == DocumentContextType.WorkOrder &&
            db.Customers.Any(customer => customer.Id == document.CustomerId && customer.OrganizationId == organizationId && customer.IsActive) &&
            db.WorkOrders.Any(order => order.Id == document.ContextId && order.OrganizationId == organizationId &&
                order.CustomerId == document.CustomerId && order.AssignedUserId == actor));
    }

    private async Task<bool> ContextBelongsToCustomerAsync(Guid organizationId, Guid customerId, DocumentContextType? type, Guid? id, CancellationToken cancellationToken)
    {
        if (type is null) return true;
        return type.Value switch
        {
            DocumentContextType.Customer => id == customerId,
            DocumentContextType.CustomerUnit => await db.CustomerUnits.AnyAsync(unit => unit.Id == id && unit.CustomerId == customerId && unit.Customer.OrganizationId == organizationId, cancellationToken),
            DocumentContextType.Quote => await db.Quotes.AnyAsync(quote => quote.Id == id && quote.CustomerId == customerId && quote.OrganizationId == organizationId, cancellationToken),
            DocumentContextType.Contract => await db.Contracts.AnyAsync(contract => contract.Id == id && contract.CustomerId == customerId && contract.OrganizationId == organizationId, cancellationToken),
            DocumentContextType.WorkOrder => await db.WorkOrders.AnyAsync(order => order.Id == id && order.CustomerId == customerId && order.OrganizationId == organizationId, cancellationToken),
            _ => false
        };
    }

    private static DocumentResponse Response(DocumentRecord document) => new(document.Id, document.CustomerId, document.OriginalFileName,
        document.ContentType, document.SizeBytes, document.Category, document.Purpose, document.Description, document.ContextType,
        document.ContextId, document.UploadedAtUtc, document.UploadedByUserId);
}
