using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Timers;

namespace SampleAPI.APIs.ProductManual;

public sealed record AskProductManualQuestion(
    [property: Required, MinLength(1), MaxLength(300)] string Question,
    Guid? DocumentId = null
);

public sealed record ProductManualQuestionAPIResponse(
    [property: Description("The question id")]
    Guid Id,
    [property: Description("The status of the operation (e.g. 'queued')")]
    string Status,
    [property: Description("The url used to retrieve the answer")]
    string StatusUrl);

public sealed record ProductManualAnswerAPIResponse(
    [property: Description("The question id returned from POST owners-manual/questions")]
    Guid Id,
    [property: Description("The original question")]
    string Question,
    [property: Description("The status of the operation (e.g. 'processing' or 'complete')")]
    string Status,
    [property: Description("The value indicating if the answer was found")]
    bool? Found,
    [property: Description("The question's answer")]
    string? Answer,
    [property: Description("An error message")]
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductManualQuestion(
    Guid Id,
    string Question,
    ProductQuestionStatus Status,
    bool Found,
    string? Answer,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? DocumentId = null
);

public sealed record ProductManualAnswer(
    string Answer,
    bool Found
);
