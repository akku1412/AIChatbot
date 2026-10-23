# AIChat – Functional Documentation

AIChat is a ChatGPT-style web application. Users sign in with an email code or Google, chat with an AI assistant,
attach documents (PDF, Word, Excel, text) and ask questions about them, and download their files or the AI's answers
as TXT, PDF, Word or Excel files. Every conversation and file is saved to the user's account.

This document explains **what the application does and how each feature works**. For installation, secrets and
deployment steps, see the [README](../README.md).

---

## Contents

1. [Architecture](#1-architecture)
2. [Feature summary](#2-feature-summary)
3. [Sign-in and sessions](#3-sign-in-and-sessions)
4. [Chat](#4-chat)
5. [File upload and document Q&A](#5-file-upload-and-document-qa)
6. [Downloads and exports](#6-downloads-and-exports)
7. [Profile](#7-profile)
8. [Data storage](#8-data-storage)
9. [API reference](#9-api-reference)
10. [Security](#10-security)
11. [Limits and configuration](#11-limits-and-configuration)
12. [Errors](#12-errors)
13. [Testing](#13-testing)
14. [Known limitations](#14-known-limitations)

---

## 1. Architecture

```
 Browser (Angular 21 SPA)
   │  HTTPS, JSON · access token in the Authorization header · refresh token in an HttpOnly cookie
   ▼
 ASP.NET Core 8 Web API (C#)
   ├─ Controllers ─ Services ─ Repositories ─ Entity Framework Core 8 ─► SQL Server  (ChatGPTCloneDB)
   ├─ File storage (private folder App_Data/uploads, outside the website root)
   ├─ Document extractors (PDF, DOCX, XLSX/XLS, TXT)   ·   Export renderers (TXT, PDF, DOCX, XLSX)
   ├─ AI provider ─► Groq (openai/gpt-oss-120b) or OpenAI, via the OpenAI .NET SDK
   └─ Email ─► Gmail SMTP (one-time sign-in codes)   ·   Google ─► Google ID-token verification
```

| Layer | Technology |
|---|---|
| Frontend | Angular 21 (standalone components, signals), TypeScript, `marked` + DOMPurify for Markdown |
| Backend | ASP.NET Core 8 Web API, C#, FluentValidation, ProblemDetails errors, rate limiting |
| Database | SQL Server 2016, Entity Framework Core 8 (code-first migrations) |
| Authentication | Email OTP, Google Sign-In, JWT access tokens, rotating refresh-token cookie |
| AI | Groq `openai/gpt-oss-120b` (development) or OpenAI, selected by configuration |
| Documents | PdfPig (read PDF), Open XML SDK (read/write Word), ExcelDataReader (read Excel), ClosedXML (write Excel), QuestPDF (write PDF), Markdig (Markdown parsing) |

**Code layout**

| Folder | Contents |
|---|---|
| `AIChatFrontend/src/app/core` | auth service, HTTP interceptor, route guard, API services, chat store, models |
| `AIChatFrontend/src/app/features` | pages: `login`, `otp-verification`, `chat` (layout + thread), `profile` |
| `AIChatFrontend/src/app/shared/components` | composer (`message-input`), `chat-message`, `attachment-chip`, sidebar, header, dialogs, toasts |
| `ChatGPTClone.Api/Controllers` | `Auth`, `Users`, `Conversations`, `Chat`, `Messages`, `Files` |
| `ChatGPTClone.Api/Services` | auth/OTP/token logic, chat + AI, conversation export, `Files/` (upload, extraction, retrieval), `Export/` (file generation) |
| `ChatGPTClone.Api/Models`, `Data` | entities, EF Core `AppDbContext`, configurations, migrations |
| `ChatGPTClone.Api.Tests` | unit and integration tests (xUnit) |

---

## 2. Feature summary

| Area | What the user can do |
|---|---|
| Sign-in | Get a 6-digit code by email and sign in with it (account is created automatically), or sign in with Google |
| Chat | Start chats, send messages, watch AI replies stream in, stop or regenerate them, copy replies and code (with syntax highlighting) |
| History | See all conversations in the sidebar (newest first), reopen them, load older messages |
| Organise | Rename and delete conversations |
| Documents | Attach up to 5 PDF / DOCX / XLSX / XLS / TXT files (20 MB each) by paperclip or drag & drop, then ask questions, get summaries and spreadsheet analysis |
| Downloads | Download any attached file in its original form |
| Exports | Download an AI reply as TXT, PDF, DOCX or XLSX; export a whole chat as TXT |
| Profile | See account details and sign-in methods, change display name, sign out |

---

## 3. Sign-in and sessions

### 3.1 Email one-time code (OTP)

1. On **/login** the user enters any email address and clicks **Send code**.
2. The API creates a random 6-digit code, stores only a keyed hash of it (HMAC, never the code itself) in
   `OtpVerifications`, and emails the code through Gmail SMTP.
3. On **/verify-otp** the user enters the code.
4. If it matches, the code is marked used and the user is signed in. **If no account exists for that email, one is
   created automatically** (no separate registration step).

Rules:

| Rule | Value |
|---|---|
| Code validity | 5 minutes |
| Wrong attempts allowed per code | 5, then that code is locked |
| Resend | allowed after 60 seconds; a new code invalidates the old one |
| Codes per email per hour | 5 (20 in development) |
| A code can be used | once |

The verify page shows attempts left, a resend countdown, and **Change email** (back to login with the address filled in).

### 3.2 Google Sign-In

1. **Continue with Google** opens Google's own sign-in; Google returns an ID token to the browser.
2. The API verifies the token with Google (signature, audience = our client id, verified email) — **no OTP needed**.
3. Account matching:
   - same Google account as before → the same user;
   - first Google sign-in with an email that already has an email-OTP account → Google is **linked** to that account
     (same chats, same files);
   - otherwise a new account is created with the Google name and photo.

### 3.3 Sessions and tokens

| Token | Lifetime | Where it lives | Purpose |
|---|---|---|---|
| Access token (JWT, HS256) | 15 minutes | browser memory only | sent as `Authorization: Bearer …` on every API call |
| Refresh token | 7 days | `HttpOnly`, `Secure`, `SameSite=Strict` cookie, path `/api/auth` | gets a new access token |

- When the access token expires, the app silently calls `/api/auth/refresh`; the refresh token is **rotated** on every
  use (old one revoked, new one issued). Re-using an old refresh token is treated as theft and revokes that session.
- Reloading the page keeps the user signed in (via the refresh cookie).
- **Sign out** revokes the refresh token and clears the cookie. Each login/device has its own row in `RefreshTokens`.
- Data is tied to the **user account**, not to a browser session: signing in again on any device shows the same chats.

---

## 4. Chat

### 4.1 Conversations

- **/chat** is a new, empty chat with suggestion cards. The conversation is created only when the first message is sent;
  the URL then becomes **/chat/{conversationId}**.
- After the first exchange the AI generates a short title (3–6 words); if that fails, the start of the first message is used.
- The sidebar lists conversations (most recently active first, paged) with a ⋯ menu: **Rename**, **Export as TXT**, **Delete**
  (delete asks for confirmation and removes all messages).
- Opening a conversation loads the newest messages; **Load earlier messages** pages backwards.

### 4.2 Sending a message

1. The composer accepts up to 8,000 characters. **Enter** sends, **Shift+Enter** adds a new line.
2. The user's message appears immediately; a typing indicator shows until the first words arrive
   ("Reading your documents…" when files are attached).
3. The API sends the AI the system instructions + the last 20 messages (within a character budget) + relevant
   document text, and **streams the reply back word by word** (Server-Sent Events) with a blinking caret.
   When it finishes, the question and reply are saved with the token usage, and the thread switches to the saved messages.
4. If the AI fails, nothing is saved: the message is removed and the typed text is put back in the composer, with an error toast.

### 4.3 Stop and regenerate

- While a reply streams, the send button becomes **Stop** (■). Stopping keeps the question and whatever was written so
  far — it is saved and still there after a reload. Stopped before any text: only the question is kept, with a
  **Generate reply** link.
- **Regenerate** (on the latest reply) streams a new answer to the same question with the same context; the old reply
  is replaced only once the new one is saved. If regeneration fails or is stopped before any text, the old reply stays.
- Only one reply can be generated per conversation at a time (a second one gets a clear "already generating" message).

### 4.4 Replies and code

- Replies are rendered as Markdown (headings, lists, tables) and sanitized with DOMPurify.
- **Code blocks** show the language, a **Copy** button and **syntax highlighting** (highlight.js) for C#, TypeScript,
  JavaScript, SQL, Python, HTML/XML, CSS/SCSS, JSON, Bash, PowerShell, YAML, Java, C++, Go and Markdown.
- The assistant is instructed to tag every code block with its language and, when debugging, to answer under
  **Problem / Cause / Fix / Why it works** headings, quoting error messages exactly; it never claims to have run code.
- Each reply has **Copy**, **Regenerate** (latest reply) and a **Download ▾** menu (see [section 6](#6-downloads-and-exports)).
- The AI provider is chosen in configuration (`Ai:Provider` = `Groq` or `OpenAI`); timeouts, rate limits and
  "context too long" errors from the provider are turned into clear messages.

---

## 5. File upload and document Q&A

### 5.1 Attaching files

- Click the **paperclip** or **drag and drop** files onto the composer.
- Each file shows as a card with a type badge (PDF / DOC / XLS / TXT), name and size, an upload progress bar, then
  "Reading document…" while the server extracts the text.
- **✕** on a card cancels an upload in progress, or deletes the uploaded file from the server if it was not sent.
- A message can be sent with files only (no text) — the AI then summarizes the files.
- Sending waits until every file is ready.

The browser checks type, size, count and empty files before uploading; the server checks everything again.

| Format | What is extracted |
|---|---|
| PDF (`.pdf`) | text per page ("Page 3"); scanned/image-only PDFs are rejected (no OCR) |
| Word (`.docx`) | paragraphs, headings (as sections), lists and tables |
| Excel (`.xlsx`, `.xls`) | every sheet's rows, plus statistics computed in .NET: row counts, totals, averages, min/max, blanks per column |
| Text (`.txt`) | full text with line breaks; encoding detected (UTF-8, UTF-16, Windows-1252); cited as "Lines 41-80" |

### 5.2 Processing pipeline

```
upload ─► size/extension check ─► saved under a random name (App_Data/uploads/{guid}.bin)
       ─► real format verified from the bytes (magic numbers; macros and renamed files rejected)
       ─► text extracted ─► split into ~2,000-character chunks ─► DocumentChunks table
       ─► status "completed" (or the upload is removed completely if anything fails)
```

### 5.3 Asking questions

- When a message is sent with attachments, the API picks the most relevant chunks of each file (keyword scoring)
  within a 24,000-character budget and adds them to the prompt, labelled with file name and page/section/sheet/lines,
  so answers can cite their source.
- Files attached earlier in the same conversation stay available for follow-up questions (up to 5 files).
- Spreadsheet numbers (totals, averages…) come from the .NET statistics, not from the AI doing arithmetic.
- **Prompt-injection protection:** document text is wrapped and marked as untrusted data, and the AI is instructed never
  to follow instructions found inside documents (tested with a document containing "ignore all previous instructions").

---

## 6. Downloads and exports

All downloads happen in the same page (no new tab): the file is fetched with the user's token and saved through a
temporary browser link. Buttons show a spinner ("Preparing PDF…") while working, and failures show the server's reason.

### 6.1 Original files

- Every file card on a sent message has a **download** button → the exact uploaded file, with its original name and type.
- Only the owner can download it; it is never served from a public URL.

### 6.2 AI reply exports

The **Download ▾** menu under each reply offers four formats. The server parses the reply's Markdown and generates a
real file in each format (not a renamed text file):

| Format | Result |
|---|---|
| **TXT** | Plain text without Markdown symbols: underlined headings, indented lists, aligned table columns. UTF-8 with BOM, Windows line endings |
| **PDF** | A4 document: title and time header, headings, paragraphs (bold/italic/code), lists, quotes, shaded code blocks, bordered tables, page numbers. Hindi and other Devanagari text render correctly (bundled Noto fonts) |
| **DOCX** | Editable Word document using Word's Heading 1–3 styles (visible in the navigation pane), real tables with a repeating header row, code and quote styles |
| **XLSX** | One worksheet per table, named after the heading above it; bold, frozen, filterable header row; numbers, ₹/$ amounts, percentages and dates stored as real Excel values. A reply with a "Label: value" list becomes an Item/Value sheet. A reply with nothing tabular returns a clear message suggesting PDF, DOCX or TXT |

File names look like `AIChat reply - <conversation title> - 2026-10-16 0701.pdf`.

### 6.3 Whole conversation

**Export chat as TXT** (download icon in the header, or ⋯ → Export as TXT in the sidebar) downloads every message of
the conversation, oldest first, with "You"/"AIChat" labels, timestamps (UTC) and attached file names —
including messages not yet loaded on screen.

---

## 7. Profile

**/profile** shows the user's name, email, photo (Google) and which sign-in methods are linked (email code, Google).
The display name can be edited (up to 200 characters). **Log out** is in the sidebar footer.

---

## 8. Data storage

Database: **`ChatGPTCloneDB`** on SQL Server (`AKANSHA\SQL2016` in development). Everything a user creates is linked to
their `UserId`; every query is filtered by it.

```
Users ─┬─< RefreshTokens            (one row per sign-in / device)
       ├─< Conversations ─< Messages ─< MessageAttachments >─┐
       └─< UploadedFiles ─< DocumentChunks                   │
              ▲───────────────────────────────────────────────┘
OtpVerifications   (keyed by email; exists before the user account)
```

| Table | Holds |
|---|---|
| `Users` | account: name, email (unique), optional GoogleId (unique), photo URL, verified flag |
| `OtpVerifications` | email, code hash and salt, expiry, attempts, used/invalidated flags |
| `RefreshTokens` | SHA-256 hash of each refresh token, expiry, revoked time (raw tokens are never stored) |
| `Conversations` | title, owner, created/updated times |
| `Messages` | role (`user` / `assistant`), content, time, AI token usage |
| `UploadedFiles` | FileId (GUID), owner, original name, private storage key, verified content type, size, status, page count |
| `DocumentChunks` | extracted text pieces with location ("Page 2", "Sheet: Sales", "Lines 1-40") for retrieval |
| `MessageAttachments` | which files were attached to which message |

Physical files live in `ChatGPTClone.Api/App_Data/uploads` under random names; the original name is only in the database.
Generated exports are **not** stored — they are created on demand.

---

## 9. API reference

Base URL in development: `https://localhost:7180` (the Angular dev server proxies `/api`). All endpoints except sign-in
require `Authorization: Bearer <access token>`. Request/response examples for Postman are in the README.

| Method | Endpoint | Purpose |
|---|---|---|
| POST | `/api/auth/send-otp` · `/api/auth/resend-otp` | email a sign-in code |
| POST | `/api/auth/verify-otp` | sign in with the code (creates the account if new) |
| POST | `/api/auth/google` | sign in with a Google ID token |
| POST | `/api/auth/refresh` · `/api/auth/logout` | new access token (rotates cookie) · sign out |
| GET | `/api/auth/me` | current user |
| GET / PUT | `/api/users/me` | profile · change name |
| GET / POST | `/api/conversations` | list (paged) · create |
| GET / PUT / DELETE | `/api/conversations/{id}` | get · rename · delete |
| GET | `/api/conversations/{id}/export` | whole chat as `.txt` |
| GET / POST | `/api/chat/{conversationId}/messages` | message history (paged) · send a message (+ `attachmentIds`) and get the AI reply |
| POST | `/api/chat/{conversationId}/messages/stream` | send a message; the reply streams as Server-Sent Events (`delta`, then `done` or `error`) |
| POST | `/api/chat/{conversationId}/messages/regenerate` | stream a new answer to the last question, replacing the last reply |
| POST | `/api/chat/{conversationId}/messages/stop` | stop the reply being generated (the partial reply is saved) |
| GET | `/api/messages/{messageId}/export?format=txt\|pdf\|docx\|xlsx` | a reply as a generated file |
| POST | `/api/files/upload` | upload one file (multipart field `file`) |
| GET | `/api/files/limits` | allowed types, max size, max files per message |
| GET | `/api/files` · `/api/files/{fileId}` | your files · one file's details |
| GET | `/api/files/{fileId}/download` | the original file |
| DELETE | `/api/files/{fileId}` | delete a file |

Swagger UI is available at `/swagger` in development.

---

## 10. Security

| Concern | How it is handled |
|---|---|
| Passwords | none are used (email codes and Google only) |
| OTP codes | random, 6 digits, stored only as an HMAC hash; 5-minute expiry, 5 attempts, single use, send limits |
| Tokens | short-lived JWT in memory; refresh token in an HttpOnly/Secure/SameSite=Strict cookie, rotated, hashed in the DB, reuse detection |
| Access control | every conversation, message and file query is filtered by the signed-in user; someone else's id returns **404** (ids reveal nothing) |
| Uploads | size limit enforced before reading the body; extension allow-list; real format checked from the bytes; macros rejected; stored under random names outside the web root; no server paths in responses |
| Downloads/exports | authenticated, owner-only, `Cache-Control: private, no-store`, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`; ids must be GUIDs/numbers, so path traversal is impossible |
| Documents and AI replies | document text is treated as untrusted (prompt-injection rules); Markdown is sanitized before display; raw HTML in replies is written as literal text in exports; formula-like text in Excel exports stays text |
| Abuse | rate limits per IP and per user (sign-in, AI messages, uploads, exports, global) |
| Headers | Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff`, Referrer-Policy; HSTS outside development |
| Secrets | in .NET user-secrets / environment variables, never in Angular files or `appsettings.json`; never logged or returned |

---

## 11. Limits and configuration

Settings are in `ChatGPTClone.Api/appsettings.json` (secrets via user-secrets or environment variables).

| Setting | Default | Meaning |
|---|---|---|
| `Jwt:AccessTokenMinutes` / `RefreshTokenDays` | 15 / 7 | token lifetimes |
| `Otp:ExpiryMinutes` / `MaxAttempts` / `ResendCooldownSeconds` / `MaxSendsPerHour` | 5 / 5 / 60 / 5 | email-code rules |
| `Ai:Provider` | `OpenAI` (`Groq` in development) | which AI service is used |
| `Ai:MaxHistoryMessages` / `MaxOutputTokens` / `TimeoutSeconds` | 20 / 2048 / 90 | AI context and reply limits |
| `FileUploads:MaxFileSizeBytes` / `MaxFilesPerMessage` | 20 MB / 5 | upload limits (mirrored in the UI) |
| `FileUploads:ChunkCharacters` / `MaxDocumentContextCharacters` | 2,000 / 24,000 | document retrieval sizes |
| `RateLimits:GlobalPerMinute` / `AuthPerMinute` / `AiChatPerMinute` / `UploadsPerMinute` / `ExportsPerMinute` | 100 / 10 / 20 / 20 / 30 | requests per minute |
| Message length | 8,000 characters | per user message |
| Conversation title | 200 characters | rename limit |

---

## 12. Errors

All API errors use the standard `application/problem+json` format with `title`, `detail` and often a machine-readable
`reason`. The UI shows the `detail` text in a toast or inline message.

| Status | Typical cause |
|---|---|
| 400 | invalid input (validation errors), wrong/expired OTP (with `attemptsRemaining`), unknown export format |
| 401 | not signed in / session expired |
| 404 | item doesn't exist **or belongs to someone else** |
| 409 | file still processing |
| 413 | file larger than 20 MB, or the AI context is too long |
| 415 | unsupported file type, or content doesn't match the extension |
| 422 | file can't be read (password-protected, scanned, empty), or XLSX export of a reply with no table |
| 429 | rate limit reached (`retryAfterSeconds`) |
| 502 / 503 / 504 | the AI provider failed, is unavailable, or timed out |

---

## 13. Testing

| Suite | Command | Covers |
|---|---|---|
| Backend (165 tests) | `dotnet test ChatGPTClone.Api.Tests` | OTP and Google sign-in flows, tokens and cookies, ownership (404 for other users), rate limits, chat and AI context, document extraction for every format, uploads and validation, downloads, all four export formats (files re-opened and validated with independent readers), security headers |
| Frontend (45 tests) | `cd AIChatFrontend; npx ng test --watch=false` | composer (Enter/Shift+Enter, uploads, cancel, remove), downloads and exports, error messages, file-name helpers |

Integration tests run the real API against a temporary database (`ChatGPTCloneDB_Tests`) with fake email, Google and
AI services. Features were also tested end-to-end in the browser with the real Groq model; exported DOCX and XLSX files
were opened in Microsoft Word and Excel, and PDFs in the Windows PDF renderer.

---

## 14. Known limitations

- Scanned (image-only) PDFs are rejected — OCR is not implemented. Old Word `.doc` files are not supported.
- `.xls` (Excel 97–2003) is supported by the code but has not been tested with a real `.xls` file.
- Uploaded files are stored on the local disk; production should use private cloud storage (e.g. Azure Blob),
  add malware scanning, and clean up files that were uploaded but never sent.
- **Stop** is delivered through an in-memory registry: with several API instances behind a load balancer, a stop
  request may reach a different instance; the browser then closes the stream itself, which also stops and saves the reply.
- Export timestamps are in UTC.
- QuestPDF (used for PDF exports) is free under its Community License for individuals and companies under
  USD 1M revenue; larger organisations need a paid license.
