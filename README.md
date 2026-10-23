# AIChat

A ChatGPT-style chat app: **Angular 21** frontend, **ASP.NET Core 8** Web API, **SQL Server**, EF Core, and an
OpenAI-compatible AI provider (Groq or OpenAI). Sign-in is passwordless: **email one-time code** or **Google**.

**How every feature works** (sign-in, chat, documents, downloads, exports, data, security): see
[docs/FUNCTIONALITY.md](docs/FUNCTIONALITY.md). This README covers setup and deployment.

```
AIChatFrontend/          Angular app (login, code page, chat dashboard, profile)
docs/                    Functional documentation
ChatGPTClone.Api/        ASP.NET Core API (auth, conversations, chat, users)
ChatGPTClone.Api.Tests/  xUnit unit + integration tests
database/                Generated SQL schema script
publish.ps1              Builds a deployable package (API + Angular in wwwroot)
set-groq-key.cmd         Stores a Groq API key in user secrets (hidden input)
```

## Sign-in flows

| Flow | Steps | Result |
|---|---|---|
| **Email OTP** | `send-otp` → 6-digit code emailed → `verify-otp` | Session starts. A new email gets an account only after the code is verified. |
| **Google** | Google button → ID token → `google` (validated on the server) | Session starts immediately, no OTP. |

One account per email address. Signing in with Google using the email of an existing email-OTP account **links**
Google to that account (same chats). A *different* Google account claiming an already-linked email is refused (409).

A session is a 15-minute JWT access token (kept in memory by Angular) plus a 7-day refresh token in an
`HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/auth`. Refresh tokens rotate on every use and are stored
only as SHA-256 hashes; reusing a revoked one revokes all of that user's sessions.

**OTP rules:** cryptographically random 6 digits · stored as salted HMAC-SHA256 (never plain text) · expires after
5 minutes · single use · max 5 wrong attempts per code · 60-second resend cooldown · a new code invalidates the old
one · max 5 codes per email per hour (20 in Development) · auth endpoints limited to 10 requests/minute per IP ·
identical responses for new and existing addresses (no account enumeration).

## Prerequisites

- .NET SDK 8 or 9 (projects target `net8.0`), Node.js 22, SQL Server (2016+)
- `dotnet tool restore` in `ChatGPTClone.Api` installs the matching `dotnet-ef` (8.0.31)

## Local setup

### 1. Secrets (never in source control)

Secrets live in .NET **User Secrets** (`%APPDATA%\Microsoft\UserSecrets\a2b3b8dd-7264-4cb9-99ea-6a3e093c11c6\secrets.json`).
From `ChatGPTClone.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR-PC\SQL2016;Database=ChatGPTCloneDB;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "Smtp:Username"  "you@gmail.com"
dotnet user-secrets set "Smtp:FromEmail" "you@gmail.com"
dotnet user-secrets set "Smtp:Password"  "<16-character Gmail App Password, no spaces>"
dotnet user-secrets set "Otp:HashKey"    "<base64 of 32+ random bytes>"
dotnet user-secrets set "Jwt:SigningKey" "<base64 of 64 random bytes>"
```

AI key: double-click `set-groq-key.cmd` (Groq) or set `Ai:OpenAI:ApiKey` and `Ai:Provider=OpenAI`.

Generate a random base64 key in PowerShell:
`[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))`

### 2. Gmail SMTP (App Password)

1. Turn on **2-Step Verification** for the sending Google account: <https://myaccount.google.com/signinoptions/twosv>
2. Create an App Password: <https://myaccount.google.com/apppasswords> (name it "AIChat").
3. Store the 16 characters (without spaces) as `Smtp:Password`. **Never use the normal Gmail password.**

Host/port are in `appsettings.json` (`smtp.gmail.com`, `587`, STARTTLS). Any SMTP provider works: change
`Smtp:Host`/`Smtp:Port` (port 465 uses implicit TLS). Gmail allows roughly 500 messages/day; use a transactional
provider for production volumes.

### 3. Google OAuth client

1. <https://console.cloud.google.com/auth/clients> → **Create client** → *Web application*.
2. **Authorized JavaScript origins:** `http://localhost:4200`, `http://localhost`, and your production origin
   (e.g. `https://aichat.example.com`). No redirect URIs are needed (Google Identity Services popup).
3. Put the **Client ID** (public) in `ChatGPTClone.Api/appsettings.json` → `GoogleAuth:ClientId` and in
   `AIChatFrontend/src/environments/environment*.ts` → `googleClientId`. The client **secret is not used**; don't share it.
4. While the consent screen is in *Testing*, add users under **Audience → Test users**, or publish the app.

### 4. Database

```powershell
cd ChatGPTClone.Api
dotnet tool restore
dotnet ef database update
```

`database/ChatGPTCloneDB.sql` is an idempotent script of all migrations (`dotnet ef migrations script --idempotent`).

### 5. Run

```powershell
# terminal 1
cd ChatGPTClone.Api;  dotnet run --launch-profile https     # https://localhost:7180  (Swagger: /swagger)
# terminal 2
cd AIChatFrontend;    npm start                             # http://localhost:4200  (proxies /api to the API)
```

### 6. Tests

```powershell
dotnet test ChatGPTClone.Api.Tests      # 165 tests; creates and drops a temporary ChatGPTCloneDB_Tests database
cd AIChatFrontend; npx ng test --watch=false
```

Integration tests replace only Google, email and AI with fakes; everything else (EF migrations, JWT, cookies, rate
limiting, ownership checks) is real. Set `CHATGPTCLONE_TEST_DB` to use another SQL Server.

## API reference

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/send-otp` | – | `{ "email" }` → emails a code |
| POST | `/api/auth/resend-otp` | – | `{ "email" }` → new code after the cooldown |
| POST | `/api/auth/verify-otp` | – | `{ "email", "code" }` → session (registers new users) |
| POST | `/api/auth/google` | – | `{ "idToken" }` → session |
| POST | `/api/auth/refresh` | cookie | new access token, rotated cookie |
| POST | `/api/auth/logout` | cookie | revokes the refresh token |
| GET | `/api/auth/me` · `/api/users/me` | JWT | current user |
| PUT | `/api/users/me` | JWT | `{ "fullName" }` |
| GET/POST | `/api/conversations` | JWT | list (`?page&pageSize`) / create |
| GET/PUT/DELETE | `/api/conversations/{id}` | JWT | get / rename `{ "title" }` / delete |
| GET | `/api/conversations/{id}/export` | JWT | the whole conversation as a UTF-8 `.txt` download (owner only) |
| GET/POST | `/api/chat/{id}/messages` | JWT | list (`?limit&before`) / send `{ "content", "attachmentIds" }` |
| POST | `/api/chat/{id}/messages/stream` | JWT | same body; reply streamed as Server-Sent Events: `delta` `{text}` … then `done` (saved messages) or `error` |
| POST | `/api/chat/{id}/messages/regenerate` | JWT | stream a new answer to the last question (replaces the last reply) |
| POST | `/api/chat/{id}/messages/stop` | JWT | stop the running reply (204; the partial reply is saved) · 404 if nothing is running |
| POST | `/api/files/upload` | JWT | `multipart/form-data`, field `file` → extracted, returns the file record |
| GET | `/api/files/limits` | JWT | max size, max files per message, allowed extensions |
| GET | `/api/files` · `/api/files/{fileId}` | JWT | your uploads / one upload (owner only, 404 otherwise) |
| GET | `/api/files/{fileId}/download` | JWT | the original uploaded file (owner only) |
| GET | `/api/messages/{messageId}/export?format=` | JWT | a reply as a generated `txt` / `pdf` / `docx` / `xlsx` file (owner only) |
| DELETE | `/api/files/{fileId}` | JWT | deletes your upload (stored file, chunks and message links; the message text stays) |

Errors are RFC 7807 `application/problem+json` with `title`, `detail`, and sometimes `reason`,
`attemptsRemaining` or `retryAfterSeconds`.

### Postman examples

Use base URL `https://localhost:7180` (disable *SSL certificate verification* for the dev certificate) and keep
Postman's cookie jar enabled so the refresh cookie is sent to `/api/auth/refresh`.

```http
POST /api/auth/send-otp
Content-Type: application/json

{ "email": "you@gmail.com" }
```
→ `200 { "email": "you@gmail.com", "codeSent": true, "expiresInSeconds": 300, "resendAvailableInSeconds": 60, "message": "..." }`

```http
POST /api/auth/verify-otp
Content-Type: application/json

{ "email": "you@gmail.com", "code": "123456" }
```
→ `200 { "accessToken": "eyJ...", "accessTokenExpiresAt": "...", "user": { ... } }` and a `Set-Cookie: aichat_refresh=...`
→ wrong code: `400 { "title": "Incorrect code", "reason": "incorrect", "attemptsRemaining": 4 }`
→ expired/used: `400 { "reason": "expired" }` · 5 wrong: `400 { "reason": "too_many_attempts" }`

```http
GET /api/auth/me
Authorization: Bearer eyJ...
```

```http
POST /api/chat/12/messages
Authorization: Bearer eyJ...
Content-Type: application/json

{ "content": "Explain JWT refresh tokens in two sentences." }
```

Google sign-in needs a real ID token from the browser, so test it through the app (or Swagger with a token copied
from the browser's network tab for `/api/auth/google`).

Files: in Postman choose **Body → form-data**, key `file` (type *File*), then send the returned `fileId` with a message.

```http
POST /api/files/upload
Authorization: Bearer eyJ...
Content-Type: multipart/form-data; boundary=...   (Postman sets this)
```
→ `201 { "fileId": "3f2b...", "fileName": "policy.pdf", "fileExtension": ".pdf", "fileSize": 1491, "status": "completed", "pageCount": 2, ... }`
→ `415` unsupported type, or content that doesn't match the extension (e.g. an .exe renamed to .txt) ·
`422` password-protected, scanned PDF with no text, or empty · `413` larger than 20 MB · `429` more than 20 uploads a minute

```http
GET /api/conversations/12/export
Authorization: Bearer eyJ...
```
→ `200 text/plain; charset=utf-8` with `Content-Disposition: attachment; filename*=UTF-8''AIChat - <title> - 2026-10-16.txt`
(every message, oldest first) · `404` if the conversation isn't yours

```http
GET /api/files/3f2b.../download
Authorization: Bearer eyJ...
```
→ `200` the original bytes, e.g. `Content-Type: application/pdf`, `Content-Disposition: attachment; filename*=UTF-8''policy.pdf`
· `404` not yours / doesn't exist · `401` without a token. (In Postman use **Send and Download** to save the file.)

```http
GET /api/messages/57/export?format=pdf        (txt | pdf | docx | xlsx)
Authorization: Bearer eyJ...
```
→ `200` a generated file named `AIChat reply - <title> - <date>.pdf` · `400` unknown format ·
`404` not your message · `422` `"reason": "no_tabular_data"` for XLSX when the reply has no table · `429` too many exports.
The message id is `assistantMessage.messageId` in the response of `POST /api/chat/{id}/messages`.

```http
POST /api/chat/12/messages
Authorization: Bearer eyJ...
Content-Type: application/json

{ "content": "What is the leave policy?", "attachmentIds": ["3f2b..."] }
```
`content` may be empty when files are attached (the AI then summarizes them). Up to 5 files per message.

## File upload and document Q&A

- Formats: **PDF** (text-based; scanned/image-only PDFs are rejected because OCR is not implemented), **Word .docx**,
  **Excel .xlsx / .xls** (all sheets). Old `.doc` is not supported. `.xls` uses ExcelDataReader's binary reader; it is
  covered by the code path but has not been tested with a real `.xls` file yet. **Text .txt**: UTF-8 (with or without
  BOM), UTF-16 with BOM, and older ANSI (Windows-1252) files; line breaks are kept and answers cite "Lines 41-80".
  Binary content renamed to `.txt` (NUL bytes, ZIP/Office/PDF signatures) is rejected.
- Limits (`FileUploads` in `appsettings.json`, enforced on the server and mirrored in the UI via `/api/files/limits`):
  `MaxFileSizeBytes` 20 MB, `MaxFilesPerMessage` 5, `MaxExtractedCharacters`, `MaxRowsPerSheet`, `ChunkCharacters`,
  `MaxDocumentContextCharacters`, `StoragePath` (default `App_Data/uploads`, outside `wwwroot`).
- Flow: upload → magic-byte check → stored under a random name → text extracted into `DocumentChunks` → on send, the
  most relevant chunks (keyword scoring) of each attached file are added to the prompt with file/page/sheet labels.
  Files attached earlier in the same conversation stay available for follow-up questions.
- Excel: totals, averages, min/max, blanks and row counts are computed in .NET and given to the model, so numbers in
  answers come from code, not from the model doing arithmetic.
- Safety: document text is wrapped as untrusted data; the system prompt tells the model never to follow instructions
  found inside documents. File paths are never returned by the API; every file query is filtered by the owner.
- Tables: `UploadedFiles`, `DocumentChunks`, `MessageAttachments`.
- No new tables or migrations were needed for downloads/exports: originals are read from the existing private storage
  using the `StorageKey` in `UploadedFiles`, and exports are generated on demand from `Messages.Content` (nothing is stored).

## Downloads and exports

- **Original files**: every attachment on a sent message has a download button → `GET /api/files/{fileId}/download`
  returns the exact uploaded bytes with the original file name and the verified content type
  (`text/plain`, `application/pdf`, Word/Excel Open XML types). Owner only (404 for anyone else, 401 when signed out);
  the id must be a GUID, so paths can never be passed; responses are `Cache-Control: private, no-store`.
- **AI replies**: the **Download ▾** menu under each reply → `GET /api/messages/{messageId}/export?format=txt|pdf|docx|xlsx`.
  The reply's Markdown is parsed (Markdig) into headings, paragraphs, bold/italic/code, lists, quotes, code blocks and
  tables, then rendered for real in each format:
  - **TXT** – plain text without Markdown symbols; underlined headings, indented lists, aligned table columns; UTF-8 with BOM, CRLF.
  - **PDF** – A4 document (QuestPDF) with title header, headings, paragraphs, lists, shaded code blocks, bordered tables and page numbers.
  - **DOCX** – editable Word document (Open XML SDK) using Word's own Heading 1–3 styles, real tables with a repeating header row.
  - **XLSX** – one worksheet per table (ClosedXML), named after the heading above it; bold, frozen, filterable header row;
    numbers, percentages, ₹/$ amounts and ISO dates are stored as real Excel values (text that looks like a formula stays text).
    A reply without a table but with a "Label: value" list becomes an Item/Value sheet; anything else returns `422` with an explanation.
  - Raw HTML in a reply is written as literal text, never interpreted. Exports are rate limited per user
    (`RateLimits:ExportsPerMinute`, default 30).
- **Whole chat**: **Export chat as TXT** (header button or the conversation's ⋯ menu) → `GET /api/conversations/{id}/export`,
  every message oldest first (AI replies as clean plain text); times are UTC.
- **Fonts**: PDFs use the bundled Noto Sans, Noto Sans Devanagari (Hindi) and Noto Sans Mono fonts in
  `ChatGPTClone.Api/Fonts` (SIL Open Font License, `Fonts/OFL.txt`). They are copied to the build and publish output,
  so PDFs look the same on Windows and Linux servers without installing fonts.
- **Libraries**: UglyToad.PdfPig (Apache-2.0), DocumentFormat.OpenXml (MIT), ExcelDataReader (MIT), ClosedXML (MIT),
  Markdig (BSD-2-Clause), QuestPDF (**Community License**: free for individuals, and for companies under USD 1M annual
  revenue, non-profits and open source; larger companies need a paid QuestPDF license — see questpdf.com/pricing).
- Before production: move storage to a private blob container, add malware scanning of uploads, and schedule clean-up
  of uploads that were never attached to a message.

## End-to-end checklist

- [ ] Email: enter an address → **Send OTP** → code arrives (check spam) → enter it → chat opens.
- [ ] New address creates an account; the same address later signs in to the same account (chats still there).
- [ ] Wrong code shows attempts left; 5 wrong codes lock that code; **Resend** is disabled for 60 s, then works and the old code stops working.
- [ ] A code older than 5 minutes is rejected; a used code cannot be reused.
- [ ] **Change email** returns to the login page with the address filled in.
- [ ] Google: **Continue with Google** → chat opens with no code; profile shows the Google photo.
- [ ] Google with the same email as an email-OTP account → same account; Profile shows both sign-in methods.
- [ ] Reload `/chat` → still signed in; **Log out** → `/chat` redirects to login.
- [ ] Chat: send a message → AI reply (Markdown, code copy); rename and delete a conversation (with confirmation).
- [ ] Another user cannot open your conversation URL (404).
- [ ] Files: paperclip or drag & drop a PDF, DOCX, XLSX and TXT → chips show progress then size; a `.rtf` or a >20 MB file is
      refused with a message; removing a chip cancels/deletes it; ask a question → answer cites the file; reload → chips stay on the message.
- [ ] Downloads: the ⤓ button on each sent attachment saves the original file (same name, opens normally).
- [ ] Reply **Download ▾** → TXT, PDF, DOCX, XLSX each save without leaving the page ("Preparing PDF…" while it works);
      open them in Notepad, a PDF viewer, Word and Excel. XLSX on a reply without a table shows an explanation instead.
- [ ] **Export chat as TXT** saves the whole conversation; non-English text and line breaks look right in Notepad.
- [ ] Signed in as another user, a copied download/export URL returns 404.
- [ ] Mobile width: login, code page, chat drawer and profile are usable.

## Production configuration

Never put production secrets in Angular files or `appsettings.json`. Use environment variables (double underscore
for nesting) or a secret store:

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=...
Jwt__SigningKey=...            Otp__HashKey=...
Smtp__Username=...             Smtp__Password=...   Smtp__FromEmail=...
Ai__Provider=Groq|OpenAI       Ai__Groq__ApiKey=... / Ai__OpenAI__ApiKey=...
DataProtection__KeysPath=D:\aichat-keys      (persistent folder; shared by all instances)
```

Deploy:

1. `powershell -ExecutionPolicy Bypass -File .\publish.ps1` → `publish\` contains the API with the Angular app in `wwwroot`.
2. Copy `publish\` to the server (IIS with the ASP.NET Core Hosting Bundle 8, Azure App Service, or `dotnet ChatGPTClone.Api.dll` behind a reverse proxy).
3. Serve over **HTTPS** only (HSTS is on outside Development). The SPA and API share one origin, so CORS is not needed;
   leave `Cors:AllowedOrigins` empty unless another origin must call the API.
4. Apply migrations: `dotnet ef database update` from a build machine, or run `database/ChatGPTCloneDB.sql`.
5. Add the production origin to the Google OAuth client and publish the consent screen.
6. Behind a load balancer/proxy, configure forwarded headers so rate limiting sees real client IPs.

Security headers (CSP, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`) are added by `SecurityHeadersMiddleware`.
