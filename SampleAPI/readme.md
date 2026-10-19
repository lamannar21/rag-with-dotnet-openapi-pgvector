# SampleAPI

An ASP.NET Core API for uploading and indexing PDF documents and answering questions about their contents. It retrieves relevant document passages from PostgreSQL using pgvector and generates answers with an AI agent.

## Dependencies and packages

Install the .NET 10 SDK, PostgreSQL with pgvector, and obtain an OpenAI API key. Docker can be used to host the database. Node.js is needed only for the separate frontend.

From the `SampleAPI` folder, restore existing references:

```powershell
dotnet restore
```

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
  "question": "What is the override?",
  "status": "completed",
  "found": false,
  "answer": "The PDF does not contain enough information to answer this question. Try asking a different question.",
  "error": null,
  "createdAt": "2026-10-16T14:30:05.2653697+00:00",
  "updatedAt": "2026-10-16T14:30:11.2246157+00:00"
}
```

Repeat the GET until completed or failed. While queued or processing, the response supplies `Retry-After: 2`. Only completed results have a meaningful `found` value. Unknown operation IDs return 404; unknown document IDs return 404 on submission.


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

[← root readme](../readme.md) · [SampleUI readme →](../SampleUI/README.md)
