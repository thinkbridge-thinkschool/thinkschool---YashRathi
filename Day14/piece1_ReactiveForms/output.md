# Output — Day 14 Piece 1: Reactive Forms + Accessibility

---

## 1. Brief — Prompt Given to the Agent

> Build a reactive create-a-quote form for `POST /api/quotes` on the Week-1 .NET 9 API.
>
> **Endpoint:** `POST http://localhost:5000/api/quotes`
> **Auth:** Bearer JWT required (`Authorization: Bearer <token>`)
>
> **Request body fields** (from `Models/Quote.cs → Quote.Create()`):
> - `author` — string, required, max **200** characters, no whitespace-only input
> - `text` — string, required, max **1000** characters, no whitespace-only input
> - `OwnerId` — NOT a form field; auto-extracted from JWT `sub` claim server-side
>
> **Success:** `201 Created` → `{ id, author, text, createdAt }`
> **Validation failure:** `422 Unprocessable Entity` → `{ error: string }`
>
> **Requirements:**
> - Angular 21 standalone component, `ReactiveFormsModule`, `FormBuilder`
> - Validators must match the real API limits exactly — not guessed defaults
> - Custom validator: reject input that is entirely whitespace
> - Show errors only after the field is dirty or touched
> - `aria-invalid` toggled on each control when invalid
> - `aria-describedby` linking each input to its error `<span>` (only when error is shown)
> - `aria-required="true"` on both inputs
> - `role="alert"` on each error `<span>` so screen readers announce it live
> - On failed submit: call `markAllAsTouched()` then move focus to the first invalid field
> - Submitting state: disable button + set `aria-busy="true"` + change label to "Saving…"
> - Server-error state: show API error message in a `role="alert"` banner
> - Success state: show success message, reset the form

---

## 2. Agent Output — Form Component + Template

### `quotes-ui/src/app/quote-create/quote-create.component.ts`

```typescript
import {
  Component, inject, signal, ElementRef, ViewChild,
} from '@angular/core';
import {
  ReactiveFormsModule, FormBuilder, Validators,
  AbstractControl, ValidationErrors,
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
  private fb  = inject(FormBuilder);
  private svc = inject(QuoteCreateService);

  submitting    = signal(false);
  submitError   = signal<string | null>(null);
  submitSuccess = signal(false);

  form = this.fb.group({
    author: ['', [Validators.required, Validators.maxLength(200), noWhitespaceOnly]],
    text:   ['', [Validators.required, Validators.maxLength(1000), noWhitespaceOnly]],
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
      if (this.author.invalid) this.authorInput.nativeElement.focus();
      else if (this.text.invalid) this.textInput.nativeElement.focus();
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
```

### `quotes-ui/src/app/quote-create/quote-create.component.html`

```html
<section aria-labelledby="form-heading" style="max-width:520px;padding:1.5rem">

  <h2 id="form-heading" style="font-size:1.1rem;font-weight:600;margin-bottom:1.25rem">
    Add a Quote
  </h2>

  @if (submitSuccess()) {
    <div role="alert" style="color:#16a34a;margin-bottom:1rem">
      Quote added successfully.
    </div>
  }

  @if (submitError()) {
    <div role="alert" style="color:#dc2626;margin-bottom:1rem">
      Error: {{ submitError() }}
    </div>
  }

  <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>

    <!-- AUTHOR -->
    <div style="margin-bottom:1.25rem">
      <label for="author" style="display:block;font-size:0.875rem;font-weight:500;margin-bottom:4px">
        Author <span aria-hidden="true" style="color:#dc2626">*</span>
      </label>
      <input
        #authorInput id="author" type="text" formControlName="author" autocomplete="off"
        aria-required="true"
        [attr.aria-invalid]="isInvalid(author) ? 'true' : null"
        [attr.aria-describedby]="isInvalid(author) ? 'author-error' : null"
        style="width:100%;padding:8px 10px;border:1px solid;border-radius:6px;font-size:0.9rem;box-sizing:border-box"
        [style.border-color]="isInvalid(author) ? '#dc2626' : '#d1d5db'"
      />
      @if (isInvalid(author)) {
        <span id="author-error" role="alert"
              style="display:block;font-size:0.78rem;color:#dc2626;margin-top:4px">
          @if (author.errors?.['required'])           { Author is required. }
          @else if (author.errors?.['whitespaceOnly']) { Author cannot be only spaces. }
          @else if (author.errors?.['maxlength'])      { Author must be 200 characters or fewer. }
        </span>
      }
    </div>

    <!-- TEXT -->
    <div style="margin-bottom:1.25rem">
      <label for="text" style="display:block;font-size:0.875rem;font-weight:500;margin-bottom:4px">
        Quote text <span aria-hidden="true" style="color:#dc2626">*</span>
      </label>
      <textarea
        #textInput id="text" formControlName="text" rows="4"
        aria-required="true"
        [attr.aria-invalid]="isInvalid(text) ? 'true' : null"
        [attr.aria-describedby]="isInvalid(text) ? 'text-error' : null"
        style="width:100%;padding:8px 10px;border:1px solid;border-radius:6px;font-size:0.9rem;box-sizing:border-box;resize:vertical"
        [style.border-color]="isInvalid(text) ? '#dc2626' : '#d1d5db'"
      ></textarea>
      @if (isInvalid(text)) {
        <span id="text-error" role="alert"
              style="display:block;font-size:0.78rem;color:#dc2626;margin-top:4px">
          @if (text.errors?.['required'])           { Quote text is required. }
          @else if (text.errors?.['whitespaceOnly']) { Quote cannot be only spaces. }
          @else if (text.errors?.['maxlength'])      { Quote must be 1000 characters or fewer. }
        </span>
      }
    </div>

    <!-- SUBMIT -->
    <button type="submit"
      [disabled]="submitting()"
      [attr.aria-busy]="submitting() ? 'true' : null"
      style="padding:10px 24px;background:#4338ca;color:white;border:none;
             border-radius:6px;font-size:0.9rem;cursor:pointer;font-weight:500"
      [style.opacity]="submitting() ? '0.6' : '1'">
      {{ submitting() ? 'Saving…' : 'Add Quote' }}
    </button>

  </form>
</section>
```

---

## 3. Verification Log

### App — Login & Main Page

![Login Page](screenshots/01-login.png)

![Main Page — hero with dynamic subtitle "Showing 10 quotes — Page 1", quote cards grid](screenshots/02-main-page.png)

### Right Slide-In Panel — click any card

![Slide Panel — quote detail slides in from right with overlay](screenshots/03-slide-panel.png)

### Pagination — hero subtitle updates with page

![Page 2 — pill shows "2", new quotes loaded](screenshots/08-page2-hero.png)

---

### Form States Exercised

**Idle state — form before interaction**

![Form Idle](screenshots/04-form-idle.png)

**Empty submit — both fields required, focus moved to Author**

![Empty Errors — "Author is required." and "Quote text is required."](screenshots/05-form-empty-errors.png)

**Spaces-only input — custom validator fires**

![Spaces Error — "Author cannot be only spaces." and "Quote cannot be only spaces."](screenshots/06-form-spaces-error.png)

**Success — quote saved, form reset**

![Success — "Quote added successfully." banner, fields cleared](screenshots/07-form-success.png)

---

### States exercised summary

| State | How exercised | Result |
|---|---|---|
| **Empty submit** | Clicked "Add Quote" with both fields blank | Both errors shown; focus → `#author` |
| **Spaces-only** | Typed `"   "` in both fields, submitted | "cannot be only spaces" on both |
| **Submitting** | Valid input, watched button during request | Disabled + "Saving…" + `aria-busy="true"` |
| **Server error** | Forced 500 via bad auth token | `role="alert"` banner with API message |
| **Success** | Valid author + text submitted | Banner shown, fields reset |

### A11y checked

**Keyboard path:** `Tab` → Author → `Tab` → Quote text → `Tab` → Submit → `Enter` — all controls reachable in logical order, no focus trap.

**axe-core 4.7.2** via Playwright headless:
- Before fixes: 1 violation (color-contrast), 2 incomplete (aria-valid-attr-value critical + color-contrast)
- After fixes: **0 violations, 0 critical incomplete**

---

### ONE concrete bug caught and fixed

**Bug: `aria-describedby` hardcoded — dead IDREF when field is valid (axe: critical)**

The agent generated a static attribute always present on the input:

```html
<!-- AGENT FIRST DRAFT — always present even when error span is not in DOM -->
aria-describedby="author-error"
```

When the field is valid, `<span id="author-error">` is not rendered. The attribute points to a non-existent element — axe flagged this as `aria-valid-attr-value` / **critical**. Screen readers would attempt to announce a missing element or read "author-error" as orphaned literal text, breaking error linkage.

**Fix:**
```html
<!-- FIXED — only wired when the error span actually exists in the DOM -->
[attr.aria-describedby]="isInvalid(author) ? 'author-error' : null"
```

Same fix applied to the `text` field.

*(Two additional bugs also fixed: `maxLength(100/500)` → `maxLength(200/1000)` to match `Quote.Create()`, and button contrast `#6366f1` → `#4338ca` to pass WCAG AA 4.5:1.)*

---

### What breaks if the quote contract changes

| API change | What breaks in this form |
|---|---|
| Field renamed `text` → `content` | `formControlName="text"` stops binding; submits empty with no error |
| New required field added (e.g. `category`) | No control or validator — API returns `422`, form shows no client error |
| `author` maxLength tightened to 50 | Client allows 200 chars → user hits server `422` with no form-level feedback |
| Error shape `{ error: string }` → `{ message: string }` | Falls back to `HTTP 422`; less informative but no crash |
| Endpoint URL changes from `/api/quotes` | `QuoteCreateService` hardcodes the path — must update manually |
