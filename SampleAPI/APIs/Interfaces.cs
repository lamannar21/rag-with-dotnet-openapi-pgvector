using Pgvector;

namespace SampleAPI.APIs.ProductManual;

public interface IQuestionAnsweringService
{
    Task<ProductManualAnswer> GetAnswerAsync(string question, CancellationToken cancellationToken = default, Guid? documentId = null);
}

public interface IKnowledgeBaseRetriever
{
    Task IndexDocumentAsync(string filename, IReadOnlyList<ManualDocumentChunk> chunks, Guid? documentId, CancellationToken cancellationToken);
    Task<bool> DocumentExistsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeChunk>> SearchPostgresVectorAsync(Vector embedding, int maxResults, CancellationToken cancellationToken, Guid? documentId = null);
}

public sealed record ManualDocumentChunk(int Page, string Text);

public interface IQuestionMatchingService
{
    Task<ProductManualAnswer> FindMatchAsync(string question, CancellationToken cancellationToken = default);
}
