export type PdfDocument = { documentId: string; filename: string; chunks: number };
export type Answer = { found: boolean; answer: string };
type Submission = { id: string; status: string; statusUrl: string };
type QuestionStatus = { status: 'queued' | 'processing' | 'completed' | 'failed'; found: boolean | null; answer: string | null; error: string | null };
const questionsUrl = '/owners-manual/questions';

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.message || body?.detail || body?.title || `Request failed (${response.status}). Please try again.`);
  }
  return response.json() as Promise<T>;
}

export async function uploadPdf(file: File, signal: AbortSignal): Promise<PdfDocument> {
  const form = new FormData();
  form.append('file', file);
  return read(await fetch(`${questionsUrl}/embeddings/upload`, { method: 'POST', body: form, signal }));
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const abort = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, milliseconds);
    signal.addEventListener('abort', abort, { once: true });
  });
}

export async function askQuestion(documentId: string, question: string, signal: AbortSignal,
  onStatus?: (status: 'queued' | 'processing') => void): Promise<Answer> {
  const submission = await read<Submission>(await fetch(`${questionsUrl}/`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, documentId }), signal,
  }));
  // Only poll this API's relative question status endpoint.
  if (submission.statusUrl !== `${questionsUrl}/${submission.id}`) throw new Error('Invalid question status URL returned by the API.');
  onStatus?.('queued');
  while (true) {
    signal.throwIfAborted();
    const response = await fetch(submission.statusUrl, { signal });
    if (response.status === 404) throw new Error('The question is no longer available. The API may have restarted. Please submit again.');
    const result = await read<QuestionStatus>(response);
    if (result.status === 'completed') {
      if (typeof result.found !== 'boolean') throw new Error('The API returned an incomplete answer. Please try again.');
      return { found: result.found, answer: result.answer ?? '' };
    }
    if (result.status === 'failed') throw new Error(result.error || 'The manual agent could not answer the question.');
    if (result.status !== 'queued' && result.status !== 'processing') throw new Error('Unknown question status returned by the API.');
    onStatus?.(result.status);
    const retryAfter = Number(response.headers.get('Retry-After') ?? '1');
    await delay(Number.isFinite(retryAfter) ? Math.min(10, Math.max(0.1, retryAfter)) * 1000 : 2000, signal);
  }
}
