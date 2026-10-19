# SampleAPI

An ASP.NET Core API for uploading and indexing PDF documents and answering questions about their contents. It retrieves relevant document passages from PostgreSQL using pgvector and generates answers with an AI agent.

## Dependencies and packages

Install the .NET 10 SDK, PostgreSQL with pgvector, and obtain an OpenAI API key. Docker can be used to host the database. Node.js is needed only for the separate frontend.

From the `SampleAPI` folder, restore existing references:

```powershell
dotnet restore
```

| Packages | Role |
| --- | --- |
| `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI` | Agent sessions and model integration |
| `OpenAI`, `Microsoft.Extensions.AI` | Model SDK and AI abstractions |
| `Npgsql`, `Pgvector` | SQL connections and vector type mapping |
| `PdfPig` | PDF text extraction |
| `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` | API schema and interactive documentation |
| `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` | Declared dependencies; current persistence uses Npgsql SQL directly, with no DbContext or EF migrations |

## API usage

| Method and route | Request | Result |
| --- | --- | --- |
| `POST /owners-manual/questions/embeddings/upload` | Multipart field `file` | Indexes PDF; returns `documentId`, `filename`, and `chunks` |
| `POST /owners-manual/questions/` | JSON `question` and optional `documentId` | HTTP 202 with `id`, `status`, and relative `statusUrl` |
| `GET /owners-manual/questions/{id}` | Operation ID | `queued`, `processing`, `completed`, or `failed` |
| `POST /owners-manual/questions/embeddings?filename=...` | PDF path on the API server | Replaces legacy document scope; returns a null document ID |

## Configuration and startup

In a terminal opened to the `SampleAPI` folder and launch the API server:

```powershell
dotnet run --launch-profile http
```

ConnectionStrings__Postgres maps to ConnectionStrings:Postgres in .NET configuration. Both variables are required. A .env file is not automatically loaded by this application. Avoid putting real secrets in tracked appsettings files.

The HTTP profile listens on http://localhost:5290 and sets ASPNETCORE_ENVIRONMENT=Development.<br> The HTTPS profile also exposes https://localhost:7089; use dotnet dev-certs https --trust if needed before `dotnet run --launch-profile https`.


## Test API

1. From the SampleAPI.http file send the first POST request. This should return the following.

```powershell
# Response
{
  "documentId": "33cc8e65-5cbd-41cb-99a4-c8fc71568bb6",
  "filename": "manual.pdf",
  "chunks": 47
}
```
2. Next, send the second POST request with the content below replacing the documentId with the documentId returned from the first POST response. 

```powershell
# POST
{
  "question": "What is the model number?",
  "documentId": "33cc8e65-5cbd-41cb-99a4-c8fc71568bb6"
}

# Response
{
  "id": "ccb1706c-b8cc-4bf0-bcf8-2a8a0e8b9f6a",
  "status": "queued",
  "statusUrl": "/owners-manual/questions/ccb1706c-b8cc-4bf0-bcf8-2a8a0e8b9f6a"
}
```
3. Next, send the GET request replacing the questionId with the id returned from the question POST. This will return one of the following:

```powershell
# agent working
{
    "id": "ccb1706c-b8cc-4bf0-bcf8-2a8a0e8b9f6a",
    "question": "What is the model number?",
    "status": "processing",
    "found": null,
    "answer": null,
    "error": null,
    "createdAt": "2026-10-16T14:41:27.7349847+00:00",
    "updatedAt": "2026-10-16T14:41:27.7351334+00:00"
}

# question found
{
  "id": "ccb1706c-b8cc-4bf0-bcf8-2a8a0e8b9f6a",
  "question": "What is the model number?",
  "status": "completed",
  "found": true,
  "answer": "The model number is PB051CCO.",
  "error": null,
  "createdAt": "2026-10-16T14:19:14.0352982+00:00",
  "updatedAt": "2026-10-16T14:19:19.1440322+00:00"
}

# question not found
{
  "id": "9d08b43f-792c-4c88-93ae-3dfc3f49ccea",
  "question": "What is the ECOS?",
  "status": "completed",
  "found": false,
  "answer": "Could you clarify what you mean by “ECOS”?",
  "error": null,
  "createdAt": "2026-10-16T14:30:05.2653697+00:00",
  "updatedAt": "2026-10-16T14:30:11.2246157+00:00"
}
```

Repeat the GET until completed or failed. While queued or processing, the response supplies `Retry-After: 2`. Only completed results have a meaningful `found` value. Unknown operation IDs return 404; unknown document IDs return 404 on submission.

## Document behavior and processing

- Browser uploads accept one nonempty PDF up to 10 MiB and 100,000 extracted characters. Use readable, unencrypted PDFs; no OCR is provided. Questions must contain 300 characters and cannot be whitespace only.
- Text is split into non-overlapping, page-local chunks of up to 2,000 characters. Embedding requests batch up to 64 chunks.
- Every upload returns a new document ID. Re-uploading the exact same filename replaces previous uploaded chunks with that filename, case-sensitively. Old IDs no longer pass the document-existence check. Their cached questions and answers are not removed by replacement.
- Different filenames retain separate scopes. Always pass the returned document ID when asking about a browser upload.
- The server-file endpoint resolves relative paths against the API content root. It replaces chunks and cached questions with `document_id IS NULL`. Questions omitting `documentId` search only this legacy scope. That endpoint does not apply the browser upload size/text limits. Avoid replacing a document while its questions are in flight.
- Original PDF bytes are not stored. Text, embeddings, and supported answers persist in PostgreSQL. Deleting cached questions does not automatically delete their answer rows.

## Source map

| File under `APIs/ProductManual` | Responsibility |
| --- | --- |
| `ProductManualAPI.cs` | Routes, validation, PDF extraction |
| `KnowledgebaseRetriever.cs` | Embeddings, transactional indexing, and scoped cosine retrieval |
| `ProductManualAgent.cs` | Cache lookup at similarity >= 0.75, five-chunk retrieval, and agent response |
| `QuestionAnswerRepository.cs` | Supported-answer cache and similar-question lookup |
| `QuestionQueue.cs` | Queue capacity of 100 and in-memory operation status |
| `QuestionWorker.cs` | Background worker registered by `Program.cs` |
| `DTOs.cs` | Request and response contracts |

The agent receives up to 20,000 grounding characters and is instructed to keep answers within 120 words. The word limit is a prompt instruction, not output validation. Source citation markers are stripped before returning answers. Only answers with `Found = true` are cached.

## Verification and troubleshooting

Upload a real text PDF and poll a question to exercise the database, embeddings, and answer model.

| Symptom | Check |
| --- | --- |
| Missing configuration | Set both environment variables in the shell running the API |
| Database connection failure | Container status, host port, database name, and matching password |
| Vector extension unavailable | Install pgvector on PostgreSQL; NuGet alone is insufficient |
| Missing table or `document_id` | Initialize the correct database; existing schemas need a separate migration |
| Model access or quota error | API key, model access, and account usage limits |
| No readable PDF text | Use a text PDF or perform OCR first |
| Question status disappears | Restarting the API loses operations; submit again |


<table width="100%">
  <tr>
    <td align="left"><a href="../readme.md">← Back: root readme</a></td>
    <td align="right"><a href="../SampleUI/readme.md">Next: SampleUI readme →</a></td>
  </tr>
</table>
