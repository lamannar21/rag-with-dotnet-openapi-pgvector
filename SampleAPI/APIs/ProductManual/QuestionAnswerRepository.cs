using Npgsql;
using SampleAPI.APIs.ProductManual;
using Pgvector;

public sealed class QuestionAnswerRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public QuestionAnswerRepository(
        string connectionString)
    {
        var dataSourceBuilder =
            new NpgsqlDataSourceBuilder(connectionString);

        dataSourceBuilder.UseVector();

        _dataSource = dataSourceBuilder.Build();
    }

    public async Task<long> AddAsync(
        string question,
        string answer,
        Vector embedding,
        CancellationToken cancellationToken = default,
        Guid? documentId = null)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        await using var connection =
            await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        //
        // Insert the answer first.
        //
        await using var answerCommand =
            new NpgsqlCommand(
                """
                INSERT INTO answer (answer_text)
                VALUES ($1)
                RETURNING answer_id;
                """,
                connection,
                transaction);

        answerCommand.Parameters.AddWithValue(answer);

        long answerId =
            (long)(await answerCommand.ExecuteScalarAsync(cancellationToken))!;

        //
        // Insert the question and its embedding.
        //
        await using var questionCommand =
            new NpgsqlCommand(
                """
                INSERT INTO question
                (
                    answer_id,
                    question_text,
                    question_embedding,
                    embedding_model,
                    document_id
                )
                VALUES ($1, $2, $3, $4, $5);
                """,
                connection,
                transaction);

        questionCommand.Parameters.AddWithValue(answerId);
        questionCommand.Parameters.AddWithValue(question);
        questionCommand.Parameters.AddWithValue(embedding);
        questionCommand.Parameters.AddWithValue(
            QuestionEmbeddingService.Model);
        questionCommand.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)documentId ?? DBNull.Value);

        await questionCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return answerId;
    }

    public async Task<QuestionMatch?> FindClosestAsync(
        Vector embedding,
        CancellationToken cancellationToken = default,
        Guid? documentId = null)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        await using var connection =
            await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    q.question_id,
                    q.question_text,
                    a.answer_text,
                    1 - (q.question_embedding <=> $1)
                        AS similarity
                FROM question q
                INNER JOIN answer a
                    ON a.answer_id = q.answer_id
                WHERE q.document_id IS NOT DISTINCT FROM $2
                ORDER BY
                    q.question_embedding <=> $1
                LIMIT 1;
                """,
                connection);

        command.Parameters.AddWithValue(embedding);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)documentId ?? DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        Console.WriteLine("Found question match in Postgres");
        return new QuestionMatch(
            QuestionId: reader.GetInt64(0),
            Question: reader.GetString(1),
            Answer: reader.GetString(2),
            Similarity: reader.GetDouble(3));
    }
}

public sealed record QuestionMatch(
    long QuestionId,
    string Question,
    string Answer,
    double Similarity);
