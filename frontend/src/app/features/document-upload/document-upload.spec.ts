import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import {
  CaseDocument,
  DocumentProcessingResult,
} from '../../core/models/document.model';
import { DocumentService, DocumentUploadEvent } from '../../core/services/document.service';
import { makeDocument, makePdfFile } from '../../testing/document-fixtures';
import { DocumentUpload } from './document-upload';

describe('DocumentUpload', () => {
  const caseId = '00000000-0000-0000-0000-000000000123';
  let uploadDocument: ReturnType<typeof vi.fn>;
  let processDocument: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<DocumentUpload>;

  async function setup(): Promise<void> {
    uploadDocument = vi.fn();
    // The happy path by default: uploaded documents come back processed.
    processDocument = vi.fn(
      (_caseId: string, documentId: string) =>
        of({
          document: makeDocument({ id: documentId, processingStatus: 'Normalized' }),
          failureReason: null,
        }),
    );
    TestBed.configureTestingModule({
      imports: [DocumentUpload],
      providers: [{ provide: DocumentService, useValue: { uploadDocument, processDocument } }],
    });
    fixture = TestBed.createComponent(DocumentUpload);
    fixture.componentRef.setInput('caseId', caseId);
    await fixture.whenStable();
  }

  /** Makes the upload succeed, returning the document the API stored. */
  function uploadSucceedsWith(document: CaseDocument): void {
    uploadDocument.mockReturnValue(of({ kind: 'complete', document }));
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function selectFiles(...files: File[]): Promise<void> {
    const input = el().querySelector<HTMLInputElement>('input[type="file"]');
    expect(input).not.toBeNull();
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input!.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  async function clickUpload(): Promise<void> {
    const button = [...el().querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Upload',
    );
    expect(button).toBeDefined();
    button!.click();
    await fixture.whenStable();
  }

  async function clickRetry(): Promise<void> {
    const button = [...el().querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Retry',
    );
    expect(button).toBeDefined();
    button!.click();
    await fixture.whenStable();
  }

  it('adds a row per selected PDF with the default document type', async () => {
    await setup();

    await selectFiles(makePdfFile('site-plan.pdf'), makePdfFile('affidavit.pdf'));

    const rows = el().querySelectorAll('.upload-item');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('site-plan.pdf');
    expect(rows[1].textContent).toContain('affidavit.pdf');

    const select = rows[0].querySelector<HTMLSelectElement>('select');
    expect(select?.value).toBe('SupportingDocument');
    expect(uploadDocument).not.toHaveBeenCalled();
  });

  it('rejects non-PDF files with a notice', async () => {
    await setup();

    await selectFiles(new File(['x'], 'photo.heic', { type: 'image/heic' }));

    expect(el().querySelectorAll('.upload-item')).toHaveLength(0);
    expect(el().textContent).toContain('Skipped (PDF only): photo.heic');
  });

  it('adds rows for files dropped on the dropzone', async () => {
    await setup();

    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', {
      value: { files: [makePdfFile('dropped.pdf')] },
    });
    el().querySelector('.dropzone')!.dispatchEvent(drop);
    await fixture.whenStable();

    expect(el().querySelectorAll('.upload-item')).toHaveLength(1);
    expect(el().textContent).toContain('dropped.pdf');
  });

  it('uploads each ready file through the service with its selected type', async () => {
    await setup();
    uploadDocument.mockImplementation(
      (_caseId: string, file: File): ReturnType<DocumentService['uploadDocument']> =>
        of({ kind: 'complete', document: makeDocument({ fileName: file.name }) }),
    );

    const first = makePdfFile('site-plan.pdf');
    const second = makePdfFile('affidavit.pdf');
    await selectFiles(first, second);

    const selects = el().querySelectorAll<HTMLSelectElement>('.upload-item select');
    selects[0].value = 'SitePlan';
    selects[0].dispatchEvent(new Event('change'));
    await fixture.whenStable();

    await clickUpload();

    expect(uploadDocument).toHaveBeenCalledTimes(2);
    expect(uploadDocument).toHaveBeenCalledWith(caseId, first, 'SitePlan');
    expect(uploadDocument).toHaveBeenCalledWith(caseId, second, 'SupportingDocument');
    expect(el().textContent).toContain('Processed');
  });

  it('emits uploaded for each stored document', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);

    const emitted: CaseDocument[] = [];
    fixture.componentInstance.uploaded.subscribe((document) => emitted.push(document));

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(emitted).toEqual([stored]);
  });

  it('processes each stored document so it does not sit at Uploaded', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(processDocument).toHaveBeenCalledTimes(1);
    expect(processDocument).toHaveBeenCalledWith(caseId, stored.id);
  });

  it('emits processed with the document its run produced', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    const normalized = { ...stored, processingStatus: 'Normalized' as const };
    uploadSucceedsWith(stored);
    processDocument.mockReturnValue(of({ document: normalized, failureReason: null }));

    const emitted: CaseDocument[] = [];
    fixture.componentInstance.processed.subscribe((document) => emitted.push(document));

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(emitted).toEqual([normalized]);
  });

  it('shows a processing state while the pipeline runs', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);
    const runs = new Subject<DocumentProcessingResult>();
    processDocument.mockReturnValue(runs);

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(el().textContent).toContain('Processing');
    expect(el().textContent).not.toContain('Processed');

    runs.next({
      document: { ...stored, processingStatus: 'Normalized' },
      failureReason: null,
    });
    await fixture.whenStable();

    expect(el().textContent).toContain('Processed');
  });

  it('reports a failed run with its reason instead of claiming success', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);
    processDocument.mockReturnValue(
      of({
        document: { ...stored, processingStatus: 'Failed' as const },
        failureReason: 'The file could not be analyzed: unexpected end of stream.',
      }),
    );

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(el().textContent).toContain('Failed');
    expect(el().textContent).toContain('could not be analyzed');
    expect(el().textContent).not.toContain('Processed');
  });

  it('emits processed even when the run failed, so the case shows the failure', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    const failed = { ...stored, processingStatus: 'Failed' as const };
    uploadSucceedsWith(stored);
    processDocument.mockReturnValue(of({ document: failed, failureReason: 'Extraction outage.' }));

    const emitted: CaseDocument[] = [];
    fixture.componentInstance.processed.subscribe((document) => emitted.push(document));

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(emitted).toEqual([failed]);
  });

  it('retries only processing for a document that is already stored', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);
    processDocument.mockReturnValue(
      of({
        document: { ...stored, processingStatus: 'Failed' as const },
        failureReason: 'Extraction outage.',
      }),
    );

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();
    expect(el().textContent).toContain('Extraction outage.');

    processDocument.mockReturnValue(
      of({ document: { ...stored, processingStatus: 'Normalized' as const }, failureReason: null }),
    );
    await clickRetry();

    // Re-uploading would duplicate a file the API already stored.
    expect(uploadDocument).toHaveBeenCalledTimes(1);
    expect(processDocument).toHaveBeenCalledTimes(2);
    expect(el().textContent).toContain('Processed');
    expect(el().textContent).not.toContain('Retry');
  });

  it('keeps the upload and explains when the processing request itself fails', async () => {
    await setup();
    const stored = makeDocument({ fileName: 'site-plan.pdf' });
    uploadSucceedsWith(stored);
    processDocument.mockReturnValue(throwError(() => new Error('offline')));

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(el().textContent).toContain('The file was uploaded, but processing could not be started.');
    expect(el().textContent).not.toContain('Upload failed.');
  });

  it('shows upload progress while the request is in flight', async () => {
    await setup();
    const events = new Subject<DocumentUploadEvent>();
    uploadDocument.mockReturnValue(events);

    await selectFiles(makePdfFile());
    await clickUpload();

    events.next({ kind: 'progress', percent: 40 });
    await fixture.whenStable();

    const bar = el().querySelector<HTMLElement>('.upload-item__progress-bar');
    expect(bar?.style.width).toBe('40%');
    expect(el().textContent).toContain('40%');
  });

  it('marks a failed upload and retries it through the service', async () => {
    await setup();
    uploadDocument.mockReturnValue(throwError(() => new Error('boom')));

    await selectFiles(makePdfFile('site-plan.pdf'));
    await clickUpload();

    expect(el().textContent).toContain('Failed');
    expect(el().textContent).toContain('Upload failed. Check the file and try again.');
    expect(processDocument).not.toHaveBeenCalled();

    uploadSucceedsWith(makeDocument({ fileName: 'site-plan.pdf' }));
    await clickRetry();

    // Nothing was stored, so the retry repeats the upload itself.
    expect(uploadDocument).toHaveBeenCalledTimes(2);
    expect(el().textContent).toContain('Processed');
    expect(el().textContent).not.toContain('Retry');
  });

  it('removes a pending row without uploading it', async () => {
    await setup();

    await selectFiles(makePdfFile('site-plan.pdf'));

    const remove = [...el().querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Remove',
    );
    remove!.click();
    await fixture.whenStable();

    expect(el().querySelectorAll('.upload-item')).toHaveLength(0);
    expect(uploadDocument).not.toHaveBeenCalled();
  });
});
