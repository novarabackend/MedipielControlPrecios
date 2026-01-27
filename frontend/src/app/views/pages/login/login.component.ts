import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardGroupComponent,
  ColComponent,
  ContainerComponent,
  FormControlDirective,
  FormDirective,
  InputGroupComponent,
  InputGroupTextDirective,
  RowComponent
} from '@coreui/angular';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ContainerComponent,
    RowComponent,
    ColComponent,
    CardGroupComponent,
    CardComponent,
    CardBodyComponent,
    FormDirective,
    InputGroupComponent,
    InputGroupTextDirective,
    IconDirective,
    FormControlDirective,
    ButtonDirective,
    ReactiveFormsModule
  ]
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);

  readonly step = signal<'request' | 'verify'>('request');
  readonly emailPreview = signal('');

  readonly emailForm = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.pattern(/^[^@\s]+@medipiel\.co$/i)]]
  });

  readonly otpForm = this.fb.group({
    code: ['', [Validators.required]]
  });

  onSendCode(): void {
    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }
    this.emailPreview.set(this.emailForm.value.email ?? '');
    this.step.set('verify');
  }

  onBack(): void {
    this.step.set('request');
  }

  onVerify(): void {
    if (this.otpForm.invalid) {
      this.otpForm.markAllAsTouched();
      return;
    }
  }
}
