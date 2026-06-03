import {
  Component,
  inject,
  signal,
  ElementRef,
  ViewChild,
} from '@angular/core';
import {
  ReactiveFormsModule,
  FormBuilder,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { QuoteCreateService } from './quote-create.service';
import { CreateQuotePayload } from './quote-create.types';

function noWhitespaceOnly(ctrl: AbstractControl): ValidationErrors | null {
  const v = ctrl.value as string;
  return v && v.trim().length === 0 ? { whitespaceOnly: true } : null;
}

@Component({
  selector: 'app-quote-create',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './quote-create.component.html',
})
export class QuoteCreateComponent {
  private fb = inject(FormBuilder);
  private svc = inject(QuoteCreateService);

  submitting   = signal(false);
  submitError  = signal<string | null>(null);
  submitSuccess = signal(false);

  form = this.fb.group({
    author: ['', [
      Validators.required,
      Validators.maxLength(200),  // API: Quote.Create() enforces max 200
      noWhitespaceOnly,
    ]],
    text: ['', [
      Validators.required,
      Validators.maxLength(1000), // API: Quote.Create() enforces max 1000
      noWhitespaceOnly,
    ]],
  });

  @ViewChild('authorInput') authorInput!: ElementRef<HTMLInputElement>;
  @ViewChild('textInput')   textInput!: ElementRef<HTMLTextAreaElement>;

  get author(): AbstractControl { return this.form.controls.author; }
  get text(): AbstractControl   { return this.form.controls.text; }

  isInvalid(ctrl: AbstractControl): boolean {
    return ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  onSubmit(): void {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      if (this.author.invalid) {
        this.authorInput.nativeElement.focus();
      } else if (this.text.invalid) {
        this.textInput.nativeElement.focus();
      }
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);
    this.submitSuccess.set(false);

    const payload: CreateQuotePayload = {
      author: this.form.value.author!.trim(),
      text:   this.form.value.text!.trim(),
    };

    this.svc.createQuote(payload).subscribe({
      next: () => {
        this.submitting.set(false);
        this.submitSuccess.set(true);
        this.form.reset();
      },
      error: (e: Error) => {
        this.submitting.set(false);
        this.submitError.set(e.message);
      },
    });
  }
}
