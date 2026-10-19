using Npgsql;
using OpenAI.Embeddings;
using Pgvector;

namespace SampleAPI.APIs.ProductManual;

public sealed record KnowledgeChunk(
    string Text,
    string? SourceUri,
    double? Score);

public sealed class KnowledgeBaseRetriever(IConfiguration configuration) : IKnowledgeBaseRetriever
{
    /// <summary>
    /// Searches document_chunk in Postgres by cosine similarity.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchPostgresVectorAsync(
        Vector embedding,
        int maxResults,
        CancellationToken cancellationToken,
        Guid? documentId = null)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        cancellationToken.ThrowIfCancellationRequested();

        const string model = QuestionEmbeddingService.Model;

        await using var dataSource = CreateDataSource();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT chunk_text, filename, page_number,
                   1 - (embedding <=> $1) AS similarity
            FROM document_chunk
            WHERE embedding_model = $2 AND btrim(chunk_text) <> ''
              AND document_id IS NOT DISTINCT FROM $4
            ORDER BY embedding <=> $1, document_chunk_id
            LIMIT $3;
            """,
            connection);
        command.Parameters.AddWithValue(embedding);
        command.Parameters.AddWithValue(model);
        command.Parameters.AddWithValue(maxResults);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)documentId ?? DBNull.Value);

        var chunks = new List<KnowledgeChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new KnowledgeChunk(
                reader.GetString(0),
                $"{reader.GetString(1)}#page={reader.GetInt32(2)}",
                reader.GetDouble(3)));
        }

        return chunks;
    }

    public async Task<bool> DocumentExistsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var dataSource = CreateDataSource();
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM document_chunk WHERE document_id = $1)");
        command.Parameters.AddWithValue(documentId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task IndexDocumentAsync(string filename, IReadOnlyList<ManualDocumentChunk> chunks,
        Guid? documentId, CancellationToken cancellationToken)
    {
        const string model = QuestionEmbeddingService.Model;
        var embeddingClient = new EmbeddingClient(model, configuration["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is required."));
        var embeddings = new List<Vector>(chunks.Count);
        foreach (var batch in chunks.Chunk(64))
        {
            var result = await embeddingClient.GenerateEmbeddingsAsync(
                batch.Select(chunk => chunk.Text), cancellationToken: cancellationToken);
            embeddings.AddRange(result.Value.OrderBy(embedding => embedding.Index)
                .Select(embedding => new Vector(embedding.ToFloats().ToArray())));
        }
        await using var dataSource = CreateDataSource();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Build embeddings before replacing rows so a failed indexing attempt preserves the old index.
        // The filename endpoint replaces only legacy rows.
        if (documentId is null)
        {
            await using var delete = new NpgsqlCommand("""
                DELETE FROM document_chunk WHERE document_id IS NULL;
                DELETE FROM question WHERE document_id IS NULL;
                """, connection, transaction);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            // Serialize replacements for the same filename, including simultaneous first uploads.
            // Keep this as a separate statement so the delete sees the preceding upload's commit.
            await using var uploadLock = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction);
            uploadLock.Parameters.AddWithValue(filename);
            await uploadLock.ExecuteNonQueryAsync(cancellationToken);

            await using var delete = new NpgsqlCommand("""
                DELETE FROM document_chunk
                WHERE filename = $1 AND document_id IS NOT NULL;
                """, connection, transaction);
            delete.Parameters.AddWithValue(filename);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var commands = new NpgsqlBatch(connection, transaction);
        for (int index = 0; index < chunks.Count; index++)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO document_chunk
                    (filename, chunk_index, page_number, chunk_text, embedding, embedding_model, document_id)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                """);
            command.Parameters.AddWithValue(filename);
            command.Parameters.AddWithValue(index);
            command.Parameters.AddWithValue(chunks[index].Page);
            command.Parameters.AddWithValue(chunks[index].Text);
            command.Parameters.AddWithValue(embeddings[index]);
            command.Parameters.AddWithValue(model);
            command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)documentId ?? DBNull.Value);
            commands.BatchCommands.Add(command);
        }
        await commands.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private NpgsqlDataSource CreateDataSource()
    {
        var builder = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required."));
        builder.UseVector();
        return builder.Build();
    }
}
