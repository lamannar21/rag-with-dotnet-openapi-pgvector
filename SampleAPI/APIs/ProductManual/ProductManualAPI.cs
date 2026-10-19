using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SampleAPI.APIs.ProductManual;

public static class ProductManualAPI
{
    private const int MaxFileBytes = 10 * 1024 * 1024;
    private const int MaxTextCharacters = 100_000;

    public static RouteGroupBuilder MapAskManualEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/owners-manual/questions").WithTags("Manual");
        group.MapPost("/embeddings", LoadDocument)
            .WithName("Load Document")
            .WithDescription("Loads a server-side PDF into the legacy manual index.")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/embeddings/upload", UploadDocument)
            .WithName("Upload Document")
            .WithDescription("Uploads and indexes a PDF, replacing previously uploaded chunks with the same filename (case-sensitive). Include the new returned documentId when submitting a question.")
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileBytes + 64 * 1024))
            // The existing demo does not use cookie-based authentication.
            .DisableAntiforgery();

        group.MapPost("/", SubmitQuestion)
            .WithName("Ask Manual")
            .WithDescription("Queues a question. Poll the returned statusUrl for the answer.")
            .Produces<ProductManualQuestionAPIResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);
            
        group.MapGet("/{id:guid}", GetQuestion)
            .WithName("Answer Manual Question")
            .WithDescription("Returns queued, processing, completed, or failed. Found is meaningful only when completed.")
            .Produces<ProductManualAnswerAPIResponse>()
            .Produces(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<IResult> LoadDocument(
        [FromQuery] string filename, IKnowledgeBaseRetriever retriever,
        IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return Results.BadRequest(new { message = "A filename is required." });
        if (!string.Equals(Path.GetExtension(filename), ".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "The document must be a PDF." });
        string path = Path.GetFullPath(filename, environment.ContentRootPath);
        if (!File.Exists(path)) return Results.NotFound(new { message = "The document was not found." });
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return await IndexDocument(bytes, filename, null, retriever, cancellationToken);
    }

    private static async Task<IResult> UploadDocument(
        IFormFile file, IKnowledgeBaseRetriever retriever, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxFileBytes)
            return Results.BadRequest(new { message = "Choose a non-empty PDF no larger than 10 MB." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "Only PDF files are supported." });
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return await IndexDocument(stream.ToArray(), Path.GetFileName(file.FileName), Guid.NewGuid(), retriever, cancellationToken);
    }

    // Both upload and server-file loading use the same extraction and embedding pipeline.
    private static async Task<IResult> IndexDocument(byte[] bytes, string filename, Guid? documentId,
        IKnowledgeBaseRetriever retriever, CancellationToken cancellationToken)
    {
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Results.BadRequest(new { message = "The selected file is not a valid PDF." });
        var chunks = new List<ManualDocumentChunk>();
        try
        {
            using var document = PdfDocument.Open(bytes);
            int characters = 0;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = ContentOrderTextExtractor.GetText(page);
                characters += text.Length;
                if (documentId is not null && characters > MaxTextCharacters)
                    return Results.BadRequest(new { message = "This demo supports PDFs with up to 100,000 extracted characters. Choose a shorter PDF." });
                for (int offset = 0; offset < text.Length; offset += 2000)
                {
                    string chunk = text.Substring(offset, Math.Min(2000, text.Length - offset));
                    if (!string.IsNullOrWhiteSpace(chunk)) chunks.Add(new(page.Number, chunk));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Results.BadRequest(new { message = "Unable to read this PDF. Choose an unencrypted, valid PDF." });
        }
        if (chunks.Count == 0)
            return Results.BadRequest(new { message = "This PDF has no readable text. Scanned PDFs require OCR before uploading." });
        await retriever.IndexDocumentAsync(filename, chunks, documentId, cancellationToken);
        return Results.Ok(new { documentId, filename, chunks = chunks.Count });
    }

    private static async Task<IResult> SubmitQuestion(
        AskProductManualQuestion request, ProductQuestionQueue queue,
        IKnowledgeBaseRetriever retriever, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 300)
            return Results.BadRequest(new { message = "Enter a question between 1 and 300 characters." });

        if (request.DocumentId is Guid documentId && !await retriever.DocumentExistsAsync(documentId, cancellationToken))
            return Results.NotFound(new { message = "The PDF was not found. Upload it again." });

        // queue the question so that this API can immediately return Accepted (202).
        var operation = await queue.EnqueueAsync(request.Question.Trim(), cancellationToken, request.DocumentId);
        string statusUrl = $"/owners-manual/questions/{operation.Id}";
        
        return Results.Accepted(statusUrl, new ProductManualQuestionAPIResponse(
            operation.Id, operation.Status.ToString().ToLowerInvariant(), statusUrl));
    }

    private static IResult GetQuestion(Guid id, ProductQuestionQueue queue, HttpResponse response)
    {
        if (!queue.TryGet(id, out var operation)) return Results.NotFound();
        if (operation.Status is ProductQuestionStatus.Queued or ProductQuestionStatus.Processing)
            response.Headers.RetryAfter = "2";
        return Results.Ok(new ProductManualAnswerAPIResponse(
            operation.Id, operation.Question, operation.Status.ToString().ToLowerInvariant(),
            operation.Status == ProductQuestionStatus.Completed ? operation.Found : null,
            operation.Answer, operation.Error, operation.CreatedAt, operation.UpdatedAt));
    }
}
