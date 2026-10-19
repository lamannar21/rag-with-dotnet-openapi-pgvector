import { useEffect, useRef, useState, type FormEvent } from 'react';
import { askQuestion, uploadPdf, type Answer, type PdfDocument } from './api';

export default function App() {
  const [document, setDocument] = useState<PdfDocument | null>(null);
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [busy, setBusy] = useState<'upload' | 'question' | null>(null);
  const [error, setError] = useState('');
  const [questionStatus, setQuestionStatus] = useState<'queued' | 'processing'>('queued');
  const [dragging, setDragging] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  async function load(files: FileList | null) {
    if (busy || !files?.length) return;
    setAnswer(null);
    setDocument(null);
    setError('');
    if (files.length !== 1 || !files[0].name.toLowerCase().endsWith('.pdf') || files[0].size === 0 || files[0].size > 10 * 1024 * 1024) {
      setError('Choose one non-empty PDF file no larger than 10 MB.');
      return;
    }
    const controller = new AbortController();
    request.current = controller;
    setBusy('upload');
    try { setDocument(await uploadPdf(files[0], AbortSignal.any([controller.signal, AbortSignal.timeout(120_000)]))); }
    catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : 'Upload failed.'); }
    finally { if (!controller.signal.aborted) setBusy(null); }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!document || !question.trim() || busy) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy('question'); setAnswer(null); setError(''); setQuestionStatus('queued');
    try { setAnswer(await askQuestion(document.documentId, question.trim(), AbortSignal.any([controller.signal, AbortSignal.timeout(120_000)]), setQuestionStatus)); }
    catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : 'Unable to answer the question.'); }
    finally { if (!controller.signal.aborted) setBusy(null); }
  }

  const status = busy === 'upload' ? 'Uploading and indexing PDF…' : busy === 'question' ? (questionStatus === 'queued' ? 'Question queued…' : 'Finding your answer…')
    : answer ? (answer.found ? 'Answer found' : 'Answer not found') : 'Waiting for a question';

  return <main>
    <header><span className="brand">A<span className="brand-dot">●</span></span><span className="muted">Product Manual Assistant</span><span className="demo">DEMO</span></header>
    {/* <section className="intro"><p className="eyebrow">YOUR DOCUMENTS, CLEAR ANSWERS</p><h1>Ask your product manual.</h1><p>Upload a document, ask a question, and find answers grounded in its contents.</p></section> */}
    <section className="intro"><p className="eyebrow">YOUR DOCUMENTS, CLEAR ANSWERS</p><p>Upload a document, ask a question, and find answers grounded in its contents.</p></section>
    <div className="workspace">
      <section className="card">
        <h2><span className="step">1</span> Add your document</h2>
        <div className={`dropzone ${dragging ? 'dragging' : ''}`} onDragOver={e => { e.preventDefault(); if (!busy) setDragging(true); }} onDragLeave={() => setDragging(false)} onDrop={e => { e.preventDefault(); setDragging(false); void load(e.dataTransfer.files); }}>
          <span className="file-icon" aria-hidden="true">↥</span>
          <h3>Drag & drop your PDF here</h3><p>or choose a file from your computer</p>
          <button type="button" className="secondary" disabled={!!busy} onClick={() => input.current?.click()}>Browse files</button>
          <input ref={input} aria-label="Upload PDF" type="file" accept=".pdf,application/pdf" hidden onChange={e => { void load(e.target.files); e.target.value = ''; }} />
          <small>PDF only · Up to 10 MB</small>
        </div>
        {document && <div className="loaded" role="status"><strong>✓ {document.filename}</strong><span>Ready for questions</span></div>}
        <p className="hint">Use text-based PDFs only.</p>
      </section>
      <section className="card">
        <h2><span className="step">2</span> Ask a question</h2>
        <form onSubmit={submit}>
          <label htmlFor="question">What would you like to know?</label>
          <textarea id="question" rows={6} maxLength={300} value={question} disabled={!!busy} placeholder="For example: What are the maintenance requirements?" onChange={e => { setQuestion(e.target.value); setAnswer(null); }} aria-describedby="question-help" />
          <div className="input-meta"><span id="question-help">{document ? 'Ask about the uploaded document.' : 'Upload a PDF to get started.'}</span><span>{question.length}/300</span></div>
          <button className="primary" disabled={!document || !question.trim() || !!busy} aria-busy={busy === 'question'}>
            {busy === 'question' && <span className="button-spinner" aria-hidden="true" />}
            {busy === 'question' ? 'Finding answer…' : 'Submit question →'}
          </button>
        </form>
      </section>
    </div>
    {error && <div className="error" role="alert">{error}</div>}
    <section className="card result" aria-busy={busy === 'question'}>
      <div className="result-heading"><h2>Your answer</h2><span role="status" className={`status ${answer ? answer.found ? 'found' : 'not-found' : ''}`}>{status}</span></div>
      {answer?.found ? <><label htmlFor="answer" className="sr-only">Answer</label><textarea id="answer" className="answer" readOnly value={answer.answer} rows={7} /></>
        : <p className="empty">{answer ? 'The PDF does not contain enough information to answer this question. Try asking a different question.' : 'Answers from your PDF will appear here.'}</p>}
    </section> 
    <footer>Always verify important details in the original document.</footer>
  </main>;
}
