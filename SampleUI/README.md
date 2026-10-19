# SampleUI

A React web application for the SampleAPI.

SampleUI was created with Codex using the following prompts:

```
Add a React javascript/typescript project to this solution that demonstrates using the AgentsAPI.
Include a UI element in which the user can upload a pdf file from their system.
The UI element should support drag and drop as well as browse local computer.
Add a multiline text input that allows the user to enter their question regarding the loaded pdf file.
Add a button for this text input to submit the question.
Add a UI element that shows if the answer was found or not.
Add a text box to show the answer if an answer was found.
```

## Prerequisites and packages

Commands below run from `SampleUI`.

Install the dependencies:

```powershell
npm ci
```

| Dependencies | Purpose |
| --- | --- |
| `react`, `react-dom` | Components and browser rendering |
| `typescript`, `@types/react`, `@types/react-dom`, `@types/node` | Type checking and definitions |
| `vite`, `@vitejs/plugin-react` | Development server and asset build |
| `vitest` | Test runner |
| `@testing-library/react`, `@testing-library/jest-dom`, `jsdom` | UI interactions, DOM assertions, and simulated browser |

## Run locally

Start the API using its [setup guide](../SampleAPI/readme.md), then run:

```powershell
npm run dev
```

Open http://localhost:5173. Vite uses a strict port, so startup fails if 5173 is occupied.

`vite.config.ts` proxies `/owners-manual` to http://localhost:5290. To change the API address, set this variable before starting Vite:

```powershell
$env:AGENTS_API_URL = 'http://localhost:8080'
npm run dev
```

The variable retains its original `AGENTS_API_URL` name. It is read by the Vite configuration; restart Vite after changes. The proxy also applies to preview, but does not include `/health`. Keep OpenAI keys and database credentials only in the API process.

## Using the UI

1. Drag a PDF into the upload area or choose **Browse files**.
2. Wait for **Ready for questions**, which means indexing completed.
3. Enter a question and select **Submit question**.
4. Wait for **Answer found** with a read-only answer, or **Answer not found**. Request failures appear separately as errors.

Uploads accept one nonempty PDF up to 10 MiB. The API limits uploaded extracted text to 100,000 characters and questions to 300 characters. Use readable, unencrypted PDFs; scanned files need OCR first.

Re-uploading the same filename replaces previous uploaded chunks for that exact, case-sensitive filename and returns a new document ID. Other filenames remain separate. The UI uses the new ID for subsequent questions.

## API flow

| Step | Request | Behavior |
| --- | --- | --- |
| Upload | `POST /owners-manual/questions/embeddings/upload` | Multipart field `file`; stores returned `documentId`, `filename`, and chunk count |
| Submit | `POST /owners-manual/questions/` | JSON `{ "question": "...", "documentId": "..." }`; receives HTTP 202 |
| Poll | `GET /owners-manual/questions/{id}` | Uses returned status URL until completed or failed |

`src/api.ts` validates the relative status URL and honors numeric `Retry-After` values, bounded to 0.1–10 seconds. The default interval is two seconds. Only completed responses are interpreted as found/not found.

The UI applies a two-minute timeout to uploads and question requests and cancels client requests when appropriate, including unmount. A client timeout does not cancel already queued server work. Restarting the API loses operation state; a 404 while polling requires resubmission.

## Build and test

```powershell
npm test
npm run build
npm run preview
```

`src/App.test.tsx` covers upload interactions, validation, answers, and errors. `src/api.test.ts` covers polling, worker failure, cancellation, and missing operations. Tests mock HTTP and do not require PostgreSQL, OpenAI, or a running API.

## Source layout

| File | Responsibility |
| --- | --- |
| `src/main.tsx` | React entry point |
| `src/App.tsx` | Upload/question UI, state, and timeouts |
| `src/api.ts` | Requests, polling, and response error handling |
| `src/styles.css` | Styling |
| `vite.config.ts` | React plugin, port, and proxies |
| `tsconfig.json` | TypeScript settings |

[← root readme](../readme.md)<br><br>[← SampleUI readme](../SampleAPI/readme.md)

