import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Dashboard | Harris County AI Document Review',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'cases',
    title: 'Cases | Harris County AI Document Review',
    loadComponent: () => import('./features/cases/case-list/case-list').then((m) => m.CaseList),
  },
  {
    path: 'cases/new',
    title: 'Create Case | Harris County AI Document Review',
    loadComponent: () =>
      import('./features/cases/case-create/case-create').then((m) => m.CaseCreate),
  },
  {
    path: 'cases/:id',
    title: 'Case Details | Harris County AI Document Review',
    loadComponent: () =>
      import('./features/cases/case-detail/case-detail').then((m) => m.CaseDetail),
  },
  {
    path: 'ask',
    title: 'Ask a Question | Harris County AI Document Review',
    loadComponent: () =>
      import('./features/question-answering/question-answering').then((m) => m.QuestionAnswering),
  },
  {
    path: 'knowledge-base',
    title: 'Knowledge Base | Harris County AI Document Review',
    loadComponent: () =>
      import('./features/knowledge-base/knowledge-base').then((m) => m.KnowledgeBase),
  },
  {
    path: '**',
    redirectTo: '',
  },
];
