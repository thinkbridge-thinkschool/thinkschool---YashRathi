/*
 * SIGNAL FORMS PREVIEW — HONEST ASSESSMENT
 * ==========================================
 *
 * @angular/signal-forms-preview is NOT a real npm package.
 * The Signal Forms API ships as a sub-path of the existing @angular/forms package:
 *   import { ... } from '@angular/forms/signals';
 * @angular/forms is already in package.json at ^21.2.0 — no new package needed.
 *
 * MISSING vs Reactive Forms
 * --------------------------
 * - No async validators: validateHttp() covers HTTP checks but there is no equivalent
 *   for general Promise/Observable-based async validators like RF's AsyncValidator
 * - No updateOn config: no blur/submit mode — validation recalculates on every value
 *   change; there is no way to defer evaluation to blur or form submit
 * - No FormArray equivalent: applyEach() applies a schema to array elements but there
 *   is no push()/removeAt() API; dynamic arrays require manual signal manipulation
 * - No ControlValueAccessor support: custom controls must implement FormValueControl
 *   or FormCheckboxControl instead; existing CVA components are not compatible
 * - No Angular DevTools integration: Signal Forms do not appear in the DevTools
 *   component inspector panel in Angular 21.2
 *
 * UNCERTAIN API (documented as existing, exact signature not in official docs)
 * ----------------------
 * - markAllAsTouched(): the Angular skills reference lists this method on FieldState
 *   but its signature is absent from angular.dev/api. The submit() helper is used
 *   here instead — it marks every field as touched before running the callback,
 *   achieving the same effect.
 * - pristine(): no .pristine() signal exists on FieldState; it is the inverse of
 *   dirty() and is computed as !dirty() in this component.
 */

import {
  Component,
  ElementRef,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  FormField,
  form,
  maxLength,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import { QuoteCreateService } from './quote-create.service';
import { CreateQuotePayload } from './quote-create.types';
import { isAppError } from '../app-error';

@Component({
  selector: 'app-quote-create',
  standalone: true,
  imports: [FormField],
  templateUrl: './quote-create.component.html',
})
export class QuoteCreateComponent {
  private svc = inject(QuoteCreateService);

  submitSuccess = signal(false);
  submitError   = signal<string | null>(null);
  submitted     = signal(false); // true after first submit attempt; shows errors even if fields not blurred

  // Model signal — single source of truth for all field values.
  readonly quoteModel = signal({ author: '', text: '' });

  // Signal form — FieldTree derived from the model signal.
  // The schema callback (second arg) replaces FormBuilder.group() + Validators.
  quoteForm = form(this.quoteModel, (path) => {
    required(path.author, { message: 'Author is required.' });
    maxLength(path.author, 200, { message: 'Author must be 200 characters or fewer.' });
    validate(path.author, ({ value }) => {
      const v = value();
      return v && v.trim().length === 0
        ? { kind: 'whitespaceOnly', message: 'Author cannot be only spaces.' }
        : null;
    });

    required(path.text, { message: 'Quote text is required.' });
    maxLength(path.text, 1000, { message: 'Quote must be 1000 characters or fewer.' });
    validate(path.text, ({ value }) => {
      const v = value();
      return v && v.trim().length === 0
        ? { kind: 'whitespaceOnly', message: 'Quote cannot be only spaces.' }
        : null;
    });
  });

  // Derived state — all via computed() as required.
  // These wrap the form-level FieldState signals so templates stay readable.
  formValid    = computed(() => this.quoteForm().valid());
  formInvalid  = computed(() => this.quoteForm().invalid());
  formDirty    = computed(() => this.quoteForm().dirty());
  formTouched  = computed(() => this.quoteForm().touched());
  // Signal Forms has no .pristine() — it is the inverse of dirty().
  formPristine = computed(() => !this.quoteForm().dirty());

  @ViewChild('authorInput') authorInput!: ElementRef<HTMLInputElement>;
  @ViewChild('textInput')   textInput!: ElementRef<HTMLTextAreaElement>;

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitSuccess.set(false);
    this.submitError.set(null);
    this.submitted.set(true);

    // submit() is the Signal Forms equivalent of markAllAsTouched():
    //   1. Marks every field (and the form itself) as touched.
    //   2. If the form is invalid after marking, it returns WITHOUT calling the
    //      callback — so errors become visible but no API call is made.
    //   3. If the form is valid, it sets quoteForm().submitting() = true, calls
    //      the callback, then sets submitting() = false when done.
    await submit(this.quoteForm, async () => {
      const value = this.quoteModel();
      const payload: CreateQuotePayload = {
        author: value.author.trim(),
        text:   value.text.trim(),
      };

      try {
        await firstValueFrom(this.svc.createQuote(payload));
        this.submitSuccess.set(true);
        // Reset both value and interaction state (dirty/touched → pristine).
        this.quoteForm().reset({ author: '', text: '' });
        return null;
      } catch (e: unknown) {
        if (isAppError(e)) {
          this.submitError.set(e.friendlyMessage);
        } else if (e instanceof Error) {
          this.submitError.set(e.message);
        } else {
          this.submitError.set('An unexpected error occurred.');
        }
        return null;
      }
    });

    // Focus the first invalid field after submit() has marked all as touched.
    if (this.formInvalid()) {
      if (this.quoteForm.author().invalid()) {
        this.authorInput?.nativeElement.focus();
      } else if (this.quoteForm.text().invalid()) {
        this.textInput?.nativeElement.focus();
      }
    }
  }
}
