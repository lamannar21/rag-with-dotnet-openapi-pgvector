
using SampleAPI.APIs.ProductManual;

public sealed class QuestionWorker(
    ProductQuestionQueue queue,
    IQuestionAnsweringService answerer,
    ILogger<QuestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"stoppingToken = {stoppingToken.IsCancellationRequested}");
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
