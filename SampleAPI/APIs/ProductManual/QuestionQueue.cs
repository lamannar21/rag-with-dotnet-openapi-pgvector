using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
// using Amazon.BedrockAgentRuntime.Model;

namespace SampleAPI.APIs.ProductManual;

public enum ProductQuestionStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public sealed class ProductQuestionQueue
{
    private readonly ConcurrentDictionary<Guid, ProductManualQuestion> _operations = [];
    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>
    /// Creates and enqueues a manual question operation for asynchronous processing.
    /// </summary>
    /// <param name="question"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<ProductManualQuestion> EnqueueAsync(
        string question,
        CancellationToken cancellationToken,
        Guid? documentId = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var operation = new ProductManualQuestion(
            Guid.NewGuid(),
            question,
            ProductQuestionStatus.Queued,
            false,
            null,
            null,
            now,
            now,
            documentId);

        _operations[operation.Id] = operation;

        try
        {
            await _queue.Writer.WriteAsync(operation.Id, cancellationToken);
            return operation;
        }
        catch
        {
            _operations.TryRemove(operation.Id, out _);
            throw;
        }
    }

    /// <summary>
    /// Attempts to retrieve a manual question operation by its identifier.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="operation"></param>
    /// <returns></returns>
    public bool TryGet(Guid id, [NotNullWhen(true)] out ProductManualQuestion? operation) =>
        _operations.TryGetValue(id, out operation);

    /// <summary>
    /// Reads queued manual question identifiers as they become available.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Attempts to mark a manual question operation as processing.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    internal bool TrySetProcessing(Guid id) => Update(
        id,
        operation => operation with
        {
            Status = ProductQuestionStatus.Processing,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    /// <summary>
    /// Attempts to mark a manual question operation as completed with the supplied answer.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="answer"></param>
    /// <returns></returns>
    internal bool TrySetCompleted(Guid id, ProductManualAnswer answer) => Update(
        id,
        operation => operation with
        {
            Status = ProductQuestionStatus.Completed,
            Found = answer.Found,
            Answer = answer.Answer,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    /// <summary>
    /// Attempts to mark a manual question operation as failed.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    internal bool TrySetFailed(Guid id) => Update(
        id,
        operation => operation with
        {
            Status = ProductQuestionStatus.Failed,
            Error = "The manual agent could not answer the question.",
            UpdatedAt = DateTimeOffset.UtcNow
        });

    /// <summary>
    /// Atomically applies an update to an existing manual question operation.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="update"></param>
    /// <returns></returns>
    private bool Update(
        Guid id,
        Func<ProductManualQuestion, ProductManualQuestion> update)
    {
        while (_operations.TryGetValue(id, out ProductManualQuestion? current))
        {
            if (_operations.TryUpdate(id, update(current), current))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class ProductManualQuestionWorker(
    ProductQuestionQueue queue,
    IQuestionAnsweringService answerer,
    ILogger<ProductManualQuestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid id in queue.ReadAllAsync(stoppingToken))
        {
            if (!queue.TryGet(id, out ProductManualQuestion? operation) ||
                !queue.TrySetProcessing(id))
            {
                continue;
            }

            try
            {
                ProductManualAnswer answer = await answerer.GetAnswerAsync(
                    operation.Question,
                    stoppingToken,
                    operation.DocumentId);
                queue.TrySetCompleted(id, answer);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Manual question {QuestionId} failed", id);
                queue.TrySetFailed(id);
            }
        }
    }
}
