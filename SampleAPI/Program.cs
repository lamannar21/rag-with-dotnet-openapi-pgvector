using SampleAPI.APIs.ProductManual;
using Scalar.AspNetCore;
using OpenAI.Responses;

#pragma warning disable OPENAI001

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["OPENAI_API_KEY"];
var connectionString = builder.Configuration["ConnectionStrings:Postgres"]; //"ConnectionStrings__Postgres"

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddValidation();
builder.Services.AddSingleton<IQuestionAnsweringService, ProductManualAgent>();
builder.Services.AddSingleton<ManualQuestionQueue>();
builder.Services.AddHostedService<QuestionWorker>();
builder.Services.AddSingleton<QuestionAnswerRepository>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var cs = configuration.GetConnectionString("ConnectionStrings__Postgres");

    return new QuestionAnswerRepository(
        apiKey  
            ?? throw new InvalidOperationException("OPENAI_API_KEY is required."),
        connectionString
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres is required."));
});

builder.Services.AddSingleton(new ResponsesClient(apiKey));
builder.Services.AddSingleton<KnowledgeBaseRetriever>();
builder.Services.AddSingleton<IKnowledgeBaseRetriever>(services => services.GetRequiredService<KnowledgeBaseRetriever>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .ExcludeFromDescription();
app.MapAskManualEndpoints();
app.Run();

#pragma warning restore OPENAI001

public partial class Program;
