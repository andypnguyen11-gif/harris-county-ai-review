import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { WORKFLOW_TYPE_LABELS, WorkflowType } from '../../../core/models/case.model';
import { CaseService } from '../../../core/services/case.service';

interface WorkflowOption {
  value: WorkflowType;
  label: string;
}

@Component({
  selector: 'app-case-create',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './case-create.html',
  styleUrl: './case-create.scss',
})
export class CaseCreate {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly caseService = inject(CaseService);
  private readonly router = inject(Router);

  protected readonly workflowOptions: readonly WorkflowOption[] = (
    Object.entries(WORKFLOW_TYPE_LABELS) as [WorkflowType, string][]
  ).map(([value, label]) => ({ value, label }));

  protected readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    workflowType: this.formBuilder.control<WorkflowType>('FloodplainDevelopmentPermit', [
      Validators.required,
    ]),
  });

  protected readonly submitting = signal(false);
  protected readonly submitError = signal(false);

  protected get nameControl() {
    return this.form.controls.name;
  }

  protected showNameError(): boolean {
    return this.nameControl.invalid && (this.nameControl.touched || this.nameControl.dirty);
  }

  protected onSubmit(): void {
    this.submitError.set(false);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const { name, workflowType } = this.form.getRawValue();

    this.caseService.createCase({ name: name.trim(), workflowType }).subscribe({
      next: (created) => {
        this.submitting.set(false);
        void this.router.navigate(['/cases', created.id]);
      },
      error: () => {
        this.submitting.set(false);
        this.submitError.set(true);
      },
    });
  }
}
