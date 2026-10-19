// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import App from './App';

const fetchMock = vi.fn();
const pdf = () => new File(['%PDF-1.4'], 'manual.pdf', { type: 'application/pdf' });
const uploaded = { documentId: 'test-document', filename: 'manual.pdf', chunks: 1 };
const respond = (body: unknown, status = 200) => ({ ok: status < 400, status, headers: new Headers(), json: async () => body });
const queued = () => fetchMock.mockResolvedValueOnce(respond({ id: 'question-1', status: 'queued', statusUrl: '/owners-manual/questions/question-1' }, 202));

beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

async function upload() {
  fetchMock.mockResolvedValueOnce(respond(uploaded));
  fireEvent.change(screen.getByLabelText('Upload PDF'), { target: { files: [pdf()] } });
  await screen.findByText('Ready for questions');
}

describe('PDF questions', () => {
  it('requires a PDF and a non-blank question before submission', async () => {
    render(<App />);
    expect(screen.getByRole('button', { name: /Submit question/ })).toBeDisabled();
    await upload();
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: '   ' } });
    expect(screen.getByRole('button', { name: /Submit question/ })).toBeDisabled();
  });

  it('uploads by browsing and submits the question for that document', async () => {
    render(<App />);
    await upload();
    expect(fetchMock.mock.calls[0][1].body.get('file').name).toBe('manual.pdf');
    queued();
    fetchMock.mockResolvedValueOnce(respond({ status: 'completed', found: true, answer: 'Inspect monthly. [Page 2]' }));
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: 'When should I inspect it?' } });
    fireEvent.click(screen.getByRole('button', { name: /Submit question/ }));
    expect(await screen.findByLabelText('Answer')).toHaveValue('Inspect monthly. [Page 2]');
    expect(screen.getByText('Answer found')).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe('/owners-manual/questions/embeddings/upload');
    expect(fetchMock.mock.calls[1][0]).toBe('/owners-manual/questions/');
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ question: 'When should I inspect it?', documentId: 'test-document' });
    expect(fetchMock.mock.calls[2][0]).toBe('/owners-manual/questions/question-1');
  });

  it('supports drag and drop and distinguishes a missing answer', async () => {
    render(<App />);
    fetchMock.mockResolvedValueOnce(respond(uploaded));
    fireEvent.drop(screen.getByText('Drag & drop your PDF here').parentElement!, { dataTransfer: { files: [pdf()] } });
    await screen.findByText('Ready for questions');
    queued();
    fetchMock.mockResolvedValueOnce(respond({ status: 'completed', found: false, answer: '' }));
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: 'What is not in the PDF?' } });
    fireEvent.click(screen.getByRole('button', { name: /Submit question/ }));
    await screen.findByText('Answer not found');
    expect(screen.queryByLabelText('Answer')).not.toBeInTheDocument();
  });

  it('rejects invalid files without sending them', async () => {
    render(<App />);
    fireEvent.change(screen.getByLabelText('Upload PDF'), { target: { files: [new File(['text'], 'notes.txt')] } });
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose one non-empty PDF');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows API failures as errors instead of answer not found', async () => {
    render(<App />);
    await upload();
    fetchMock.mockResolvedValueOnce(respond({ detail: 'The answering service is unavailable.' }, 502));
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: 'Question?' } });
    fireEvent.click(screen.getByRole('button', { name: /Submit question/ }));
    expect(await screen.findByRole('alert')).toHaveTextContent('answering service is unavailable');
    await waitFor(() => expect(screen.getByRole('button', { name: /Submit question/ })).toBeEnabled());
    expect(screen.queryByText('Answer not found')).not.toBeInTheDocument();
  });

  it('clears an old answer when the question changes', async () => {
    render(<App />);
    await upload();
    queued();
    fetchMock.mockResolvedValueOnce(respond({ status: 'completed', found: true, answer: 'Old answer' }));
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: 'First question' } });
    fireEvent.click(screen.getByRole('button', { name: /Submit question/ }));
    await screen.findByLabelText('Answer');
    fireEvent.change(screen.getByLabelText('What would you like to know?'), { target: { value: 'Second question' } });
    expect(screen.queryByLabelText('Answer')).not.toBeInTheDocument();
  });
});
