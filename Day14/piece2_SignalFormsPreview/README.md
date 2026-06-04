# Day 14 — Signal Forms Preview

## Overview

A full-stack quotes app rebuilt to replace the Reactive Forms create-a-quote form (piece1) with Angular's experimental **Signal Forms** API (`@angular/forms/signals`). The rest of the app — auth, quote list, pagination, slide panel — is unchanged. Only the two files inside `quote-create/` were rewritten.

**Key learning goal:** compare Signal Forms against Reactive Forms API-for-API — what changes, what is better, what is still missing.

---

## How to Run

### Prerequisites
- .NET 9 SDK
- Node.js 18+

### Step 1 — Start the backend API

```powershell
cd "Day14\piece2_SignalFormsPreview"
dotnet run
```

API runs at `http://localhost:5000`

> If port 5000 is already in use:
> ```powershell
> Get-Process -Id (Get-NetTCPConnection -LocalPort 5000).OwningProcess | Stop-Process -Force
> dotnet run
> ```

### Step 2 — Start the Angular frontend

```powershell
cd "Day14\piece2_SignalFormsPreview\quotes-ui"
npm install
npx ng serve --port 4201
```

### Step 3 — Open in browser

```
http://localhost:4201
```

Login with `test@example.com` / `password123`

---

## Screenshots

### Login Page
![Login](screenshots/01-login.png)

### Main Page — Hero + Quote Cards
![Main Page](screenshots/02-main-page.png)

### Right Slide-In Panel — Click any card
![Slide Panel](screenshots/03-slide-panel.png)

### Add Quote Form — Idle State (Signal Forms, debug panel visible)
![Form Idle](screenshots/04-form-idle.png)

### Signal Form Debug Panel — Initial State (pristine: true, invalid: true)
The debug panel confirms the five `computed()` signals at page load: form is pristine and
untouched, but already invalid because both required fields are empty.

![Debug Panel Idle](screenshots/09-debug-panel-idle.png)

> **Reading the debug panel:**
> - `pristine: true` — no user interaction yet
> - `dirty: false` — nothing typed
> - `touched: false` — no field focused + blurred
> - `valid: false` — `required()` validators failing on empty strings
> - `invalid: true` — mirrors valid; no errors shown yet because `touched = false`

### Form — Empty Submit (submit() marks all as touched, both errors show)
![Empty Errors](screenshots/05-form-empty-errors.png)

### Form — Spaces-Only Input Rejected (custom validate() fires)
![Spaces Error](screenshots/06-form-spaces-error.png)

### Form — Success State (banner shown, form reset to pristine)
![Success](screenshots/07-form-success.png)

### Pagination — Page 2
![Page 2](screenshots/08-page2-hero.png)

---

## Package Correction — Important Note

The task asked for `@angular/signal-forms-preview`.
**That package does not exist on npm** (`404 Not Found`).

The real Signal Forms API is a sub-path of the already-installed `@angular/forms`:

```
import { form, FormField, required, maxLength, validate, submit }
  from '@angular/forms/signals';
```

`@angular/forms` is already in `package.json` at `^21.2.0`. No new package was added.

---

## API Contract — `POST /api/quotes`

Defined in `Models/Quote.cs` → `Quote.Create()`:

```csharp
if (string.IsNullOrWhiteSpace(author) || author.Length > 200)
    return Result<Quote>.Fail("Author must be between 1 and 200 characters.");

if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
    return Result<Quote>.Fail("Text must be between 1 and 1000 characters.");
```

| Field | Type | Constraints |
|---|---|---|
| `author` | string | Required, max 200 chars, no whitespace-only |
| `text` | string | Required, max 1000 chars, no whitespace-only |
| `OwnerId` | auto | Extracted from JWT `sub` claim — not in request body |

**Success:** `201 Created` → `{ id, author, text, createdAt }`
**Failure:** `422 Unprocessable Entity` → `{ error: string }`

---

## Reactive Forms → Signal Forms: What Changed

| Concept | Reactive Forms (piece1) | Signal Forms (piece2) |
|---|---|---|
| Form creation | `fb.group({ author: ['', [...]] })` | `form(modelSignal, (path) => { required(path.author) })` |
| Data source | `form.value.author` | `quoteModel()` — writable signal |
| Template binding | `formControlName="author"` | `[formField]="quoteForm.author"` |
| Validity | `ctrl.invalid && ctrl.touched` | `quoteForm.author().invalid() && quoteForm.author().touched()` |
| Errors | `ctrl.errors?.['required']` | `quoteForm.author().errors()[0]?.message` |
| markAllAsTouched | `form.markAllAsTouched()` | `submit()` — marks all before running callback |
| Reset | `form.reset()` | `quoteForm().reset({ author: '', text: '' })` |
| Loading state | external `submitting` signal | `quoteForm().submitting()` built into the form |
| Module import | `ReactiveFormsModule` | `FormField` directive only |

---

## Signal Forms API — Key Facts (verified against installed `@angular/forms@21.2.15`)

| Function | Real? | Notes |
|---|---|---|
| `form(modelSignal, schemaFn)` | ✅ | Creates FieldTree from a WritableSignal |
| `FormField` directive + `[formField]` | ✅ | Confirmed in `ɵdir` declaration |
| `required(path, { message })` | ✅ | Exported from `signals.d.ts` line 216 |
| `maxLength(path, n, { message })` | ✅ | Exported from `signals.d.ts` line 139 |
| `validate(path, ctx => error\|null)` | ✅ | Exported from `signals.d.ts` line 231 |
| `submit(form, asyncFn)` | ✅ | Marks all touched, runs fn only if valid |
| `field().valid()` / `.invalid()` | ✅ | Signals on FieldState |
| `field().touched()` / `.dirty()` | ✅ | Signals on FieldState |
| `field().errors()` | ✅ | `Signal<ValidationError[]>` |
| `form().submitting()` | ✅ | Set true during submit callback |
| `form().reset(value?)` | ✅ | Resets touched/dirty; optional value |
| `field().markAsTouched()` | ✅ | Single field only |
| `markAllAsTouched()` | ❌ | Does NOT exist — use `submit()` instead |
| `field().pristine()` | ❌ | Does NOT exist — use `!field().dirty()` |
| `@angular/signal-forms-preview` | ❌ | Not on npm registry |

---

## Signal Forms — Gaps vs Reactive Forms

| Feature | Reactive Forms | Signal Forms |
|---|---|---|
| Async validators | `AsyncValidator` interface | `validateHttp()` only (no general Promise support) |
| `updateOn` config | `updateOn: 'blur'` or `'submit'` | Not available — validates on every keystroke |
| Dynamic arrays | `FormArray` + `push()` / `removeAt()` | `applyEach()` for schema; no push/remove API |
| Custom controls | `ControlValueAccessor` | Must implement `FormValueControl` — not compatible |
| Angular DevTools | Full inspector support | Not shown in DevTools component panel |
| `markAllAsTouched()` | Built-in method | Achieved via `submit()` only |

---

## Form States

| State | Behaviour |
|---|---|
| **Empty submit** | `submit()` marks all fields touched; both error messages appear; focus moves to first invalid field |
| **Spaces-only** | Custom `validate()` fires — "cannot be only spaces" |
| **Submitting** | `quoteForm().submitting()` → button label "Saving…", `aria-busy="true"` |
| **Server error** | `submitError` signal → `role="alert"` banner with API message |
| **Success** | "Quote added successfully." banner, form resets to pristine via `reset()` |

---

## Debug Panel

A collapsible panel below the form shows live Signal Form state — all five values are `computed()` signals in the component class:

```
pristine | dirty | touched | valid | invalid
```

Values are colour-coded: green = healthy state, red = invalid/active, grey = false.

---

## Project Structure

```
piece2_SignalFormsPreview/
├── screenshots/                        # Verification screenshots
├── quotes-ui/                          # Angular 21 frontend
│   └── src/app/
│       ├── app.component.*             # One-page layout (unchanged from piece1)
│       ├── quote-create/
│       │   ├── quote-create.component.ts    # ← REWRITTEN: Signal Forms
│       │   ├── quote-create.component.html  # ← REWRITTEN: [formField] + debug panel
│       │   ├── quote-create.service.ts      # Unchanged: POST /api/quotes
│       │   └── quote-create.types.ts        # Unchanged: CreateQuotePayload
│       └── auth/
│           ├── auth.service.ts              # Unchanged: JWT login/logout
│           └── auth.interceptor.ts          # Unchanged: Bearer token
├── Endpoints/                          # .NET minimal API endpoints
├── Models/Quote.cs                     # Domain model with Create() validation
├── quotes.db                           # SQLite database
└── Program.cs                          # App bootstrap
```
