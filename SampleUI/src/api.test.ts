import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { askQuestion } from './api';

const fetchMock = vi.fn();
const response = (body: unknown, status = 200, retryAfter = '2') => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', 'Retry-After': retryAfter },
});
beforeEach(() => { vi.useFakeTimers(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); });
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
function submit() {
  fetchMock.mockResolvedValueOnce(response({ id: 'q1', status: 'queued', statusUrl: '/owners-manual/questions/q1' }, 202));
}

it('polls queued and processing results, respecting Retry-After, until completed', async () => {
  submit();
  fetchMock.mockResolvedValueOnce(response({ status: 'queued', found: null }));
  fetchMock.mockResolvedValueOnce(response({ status: 'processing', found: null }));
  fetchMock.mockResolvedValueOnce(response({ status: 'completed', found: true, answer: 'Answer' }));
  const onStatus = vi.fn();
  const result = askQuestion('doc1', 'Question', new AbortController().signal, onStatus);
  await vi.advanceTimersByTimeAsync(1999);
  expect(fetchMock).toHaveBeenCalledTimes(2);
  await vi.advanceTimersByTimeAsync(1);
  expect(fetchMock).toHaveBeenCalledTimes(3);
  await vi.advanceTimersByTimeAsync(2000);
  await expect(result).resolves.toEqual({ found: true, answer: 'Answer' });
  expect(onStatus).toHaveBeenCalledWith('processing');
});

it('reports a worker failure as an error', async () => {
  submit();
  fetchMock.mockResolvedValueOnce(response({ status: 'failed', found: null, error: 'Worker failed' }));
  await expect(askQuestion('doc1', 'Question', new AbortController().signal)).rejects.toThrow('Worker failed');
});

it('stops polling when aborted', async () => {
  submit();
  fetchMock.mockResolvedValueOnce(response({ status: 'processing', found: null }));
  const controller = new AbortController();
  const result = askQuestion('doc1', 'Question', controller.signal);
  const assertion = expect(result).rejects.toThrow('Cancelled');
  await vi.advanceTimersByTimeAsync(0);
  controller.abort(new Error('Cancelled'));
  await assertion;
  await vi.advanceTimersByTimeAsync(10000);
  expect(fetchMock).toHaveBeenCalledTimes(2);
});

it('explains a missing question after an API restart', async () => {
  submit();
  fetchMock.mockResolvedValueOnce(response({}, 404));
  await expect(askQuestion('doc1', 'Question', new AbortController().signal)).rejects.toThrow('API may have restarted');
});
