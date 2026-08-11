import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CitationTarget } from '../../../core/models/citation-target.model';
import { makeCitation } from '../../../testing/question-answer-fixtures';
import { Citation } from './citation';

describe('Citation', () => {
  let fixture: ComponentFixture<Citation>;

  async function setup(citation = makeCitation(), caseId: string | null = null): Promise<void> {
    TestBed.configureTestingModule({ imports: [Citation] });
    fixture = TestBed.createComponent(Citation);
    fixture.componentRef.setInput('citation', citation);
    fixture.componentRef.setInput('caseId', caseId);
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function viewButton(): HTMLButtonElement {
    const button = el().querySelector<HTMLButtonElement>('.citation__view');
    expect(button).not.toBeNull();
    return button!;
  }

  it('renders the number, title, section and page', async () => {
    await setup(
      makeCitation({ number: 3, title: 'Floodplain Regulations', section: 'Section 4.2', page: 17 }),
    );

    expect(el().textContent).toContain('[3]');
    expect(el().textContent).toContain('Floodplain Regulations');
    expect(el().textContent).toContain('Section 4.2');
    expect(el().textContent).toContain('Page 17');
  });

  it('labels a county source as a county requirement', async () => {
    await setup(makeCitation({ source: 'County' }));

    const badge = el().querySelector('.citation__source');
    expect(badge?.textContent).toContain('County requirement');
    expect(badge?.classList.contains('citation__source--county')).toBe(true);
    expect(badge?.classList.contains('citation__source--case')).toBe(false);
  });

  it('labels a case source as an applicant submission', async () => {
    await setup(makeCitation({ source: 'Case', sourceUrl: null }));

    const badge = el().querySelector('.citation__source');
    expect(badge?.textContent).toContain('Applicant submission');
    expect(badge?.classList.contains('citation__source--case')).toBe(true);
    expect(badge?.classList.contains('citation__source--county')).toBe(false);
  });

  it('links the title out when the source has a public URL', async () => {
    await setup(makeCitation({ sourceUrl: 'https://www.hcfcd.org/regulations' }));

    const link = el().querySelector<HTMLAnchorElement>('a.citation__title');
    expect(link?.getAttribute('href')).toBe('https://www.hcfcd.org/regulations');
    expect(link?.getAttribute('rel')).toContain('noopener');
  });

  it('renders the title as plain text when there is no public URL', async () => {
    await setup(makeCitation({ sourceUrl: null }));

    expect(el().querySelector('a.citation__title')).toBeNull();
    expect(el().querySelector('.citation__title')?.textContent).toContain('Floodplain Regulations');
  });

  it('omits the page when the citation does not name one', async () => {
    await setup(makeCitation({ page: null }));

    expect(el().textContent).not.toContain('Page');
    expect(viewButton().textContent).toContain('View source');
  });

  it('names the page in the view action when the citation points at one', async () => {
    await setup(makeCitation({ page: 12 }));

    expect(viewButton().textContent).toContain('View page 12');
  });

  it('emits a county target with no case id when viewed', async () => {
    const emitted: CitationTarget[] = [];
    await setup(
      makeCitation({
        source: 'County',
        title: 'Floodplain Regulations',
        page: 17,
        sourceUrl: 'https://www.hcfcd.org/regulations',
      }),
      'case-1',
    );
    fixture.componentInstance.view.subscribe((target) => emitted.push(target));

    viewButton().click();

    expect(emitted).toHaveLength(1);
    expect(emitted[0].source).toBe('County');
    expect(emitted[0].caseId).toBeNull();
    expect(emitted[0].page).toBe(17);
    expect(emitted[0].sourceUrl).toBe('https://www.hcfcd.org/regulations');
  });

  it('emits a case target carrying the case whose documents were searched', async () => {
    const emitted: CitationTarget[] = [];
    const citation = makeCitation({ source: 'Case', title: 'application.pdf', page: 2, sourceUrl: null });
    await setup(citation, 'case-42');
    fixture.componentInstance.view.subscribe((target) => emitted.push(target));

    viewButton().click();

    expect(emitted[0]).toEqual({
      source: 'Case',
      caseId: 'case-42',
      documentId: citation.documentId,
      title: 'application.pdf',
      section: citation.section,
      page: 2,
      sourceUrl: null,
    });
  });

  it('emits a case target with a null case id when no case is known', async () => {
    const emitted: CitationTarget[] = [];
    await setup(makeCitation({ source: 'Case', sourceUrl: null }), null);
    fixture.componentInstance.view.subscribe((target) => emitted.push(target));

    viewButton().click();

    expect(emitted[0].caseId).toBeNull();
  });
});
