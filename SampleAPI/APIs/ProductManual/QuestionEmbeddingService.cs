using OpenAI.Embeddings;
using Pgvector;

namespace SampleAPI.APIs.ProductManual;

public sealed class QuestionEmbeddingService
{
    public const string Model = "text-embedding-3-small";
    private readonly EmbeddingClient _client;

    public QuestionEmbeddingService(IConfiguration configuration)
    {
        _client = new EmbeddingClient(Model, configuration["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is required."));
    }

    public async Task<Vector> CreateAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var result = await _client.GenerateEmbeddingAsync(question, cancellationToken: cancellationToken);
        return new Vector(result.Value.ToFloats().ToArray());
    }
}
