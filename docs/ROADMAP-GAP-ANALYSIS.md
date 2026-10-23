# Roadmap gap analysis

Comparison of the current AIChat code base with **"AIChat – New Functionality Roadmap"** (Oct 22, 2026, @Sunny,
23 features in 4 phases). Prepared Oct 23, 2026 from the code, the live database and test runs — before any roadmap
implementation.

## 1. Environment (confirmed, nothing upgraded)

| Item | Installed |
|---|---|
| .NET | SDK 9.0.305 (also 8.0.119); projects target **net8.0**; ASP.NET Core runtime 8.0.20 |
| EF Core | 8.0.31 (SqlServer, Design) |
| SQL Server | **2016 RTM-GDR 13.0.1742** Enterprise, database compatibility level 130; Full-Text Search **installed** |
| Angular | **21.2.17** (core), CLI/build 21.2.x, TypeScript 5.9, Vitest 4 |
| Node / npm | 22.20.0 / 10.9.3 |
| Key backend packages | OpenAI 2.14.0, PdfPig 0.1.16, DocumentFormat.OpenXml 3.5.1, ExcelDataReader 3.9.0, ClosedXML 0.105.1, QuestPDF 2026.9.1, Markdig 1.4.0, FluentValidation 12.2.0 |
| Key frontend packages | marked 18, DOMPurify 3.4, highlight.js 11.11.2 |
| AI keys configured | **Groq only** (no OpenAI key) |
| Groq models on this key | chat: `openai/gpt-oss-120b`, `openai/gpt-oss-20b`, `qwen/qwen3.8-27b` (**accepts images** — verified); speech: `whisper-large-v3`, `whisper-large-v3-turbo`; **no embedding model** |
| Local tools | Windows Defender CLI (MpCmdRun) available, real-time protection on; Docker Desktop installed (not running); **no** Tesseract, LibreOffice or ClamAV |

## 2. Baseline

- Tests: **165 backend (xUnit) + 45 frontend (Vitest) — all passing** (run Oct 23).
- Database: 9 tables, 3 migrations (last `AddFileUploadsAndAttachments`); data: 3 users, 7 conversations, 36 messages, 1 file.
- **Uncommitted work**: the streaming / stop / regenerate / syntax-highlighting work (≈ roadmap features 1–3) and the
  `docs/` folder are tested but not committed. They should be committed before roadmap work starts, so every phase has
  a clean diff.

## 3. Feature status

Legend: ✅ done · 🟡 partial (works, but differs from the roadmap) · ❌ missing

| # | Feature | Status | What exists today | Gap vs roadmap |
|---|---|---|---|---|
| 1 | Streaming replies | 🟡 | `POST /api/chat/{id}/messages/stream` (SSE `delta`/`done`/`error`), OpenAI SDK `CompleteChatStreamingAsync`, buffering disabled, rate-limited, non-streaming endpoint kept, fake-AI chunk tests | Frontend reads the stream through Angular `HttpClient` (XHR progress) instead of **fetch + ReadableStream**; no **~50 ms render throttle**; `done` lacks a separate token-usage field (it is inside `assistantMessage`) |
| 2 | Stop generating | 🟡 | Stop button; `POST …/messages/stop` + `RequestAborted` both cancel; partial reply saved | No **`IsTruncated`** column (stopped state only in the `done` event); no "(stopped)" label; client uses a stop request, not **AbortController** |
| 3 | Regenerate | 🟡 | `POST /api/chat/{id}/messages/regenerate` streams a new answer | Old reply is **deleted** — roadmap wants **versions** (`ParentMessageId`, `VersionIndex`), endpoint **`POST /api/messages/{messageId}/regenerate`**, and "< 2/3 >" arrows |
| 4 | Edit and resend | ❌ | — | `PUT /api/messages/{id}` branching, active-branch history, inline editor, version arrows |
| 5 | Search conversations | ❌ | Sidebar groups by date only | `GET /api/conversations/search?q=` (LIKE on title + content, snippet), debounced box, highlighting |
| 6 | Pin and archive | ❌ | Rename/Export/Delete in ⋯ menu | `IsPinned`, `ArchivedAt`, `PATCH /api/conversations/{id}`, `?archived=true`, Archived section |
| 7 | Vector document search | ❌ | Keyword (TF-IDF) retrieval, 24,000-char budget | `ChunkEmbeddings`, embedding provider config, hybrid ranking — **Groq has no embeddings** (decision A) |
| 8 | OCR | ❌ | Scanned PDFs rejected (`ocr_required`) | Page rendering + OCR engine, `Source = OCR` chunks, background processing with progress (decision B) |
| 9 | Image upload + vision | ❌ | — | PNG/JPG/WebP (magic bytes, 10 MB, EXIF strip), image content parts, auto-route to vision model (`qwen/qwen3.8-27b` works), thumbnails |
| 10 | More file types | 🟡 | TXT/PDF/DOCX/XLSX/XLS | **.csv** (CsvHelper + Excel stats), **.pptx** (slides + notes, "Slide 4"), **.doc** (decision D) |
| 11 | Clickable citations | ❌ | Citations are plain text "(file, Page 3)"; chunks have ids and locations | `[file:chunkId]` markers, `MessageSources`, `GET /api/files/{fileId}/chunks/{chunkId}`, chips + side panel |
| 12 | Model picker | ❌ | One model from config | `Ai:Models` allow-list, `ModelId` on Conversations/Messages, header dropdown |
| 13 | Custom instructions | ❌ | Profile edits name only | `UserSettings`, `GET/PUT /api/users/me/settings`, prompt placement below safety rules |
| 14 | Projects | ❌ | — | `Projects`, `ProjectFiles`, `Conversations.ProjectId`, CRUD, retrieval over project files, sidebar folders |
| 15 | Share read-only link | ❌ | — | `SharedLinks` (token hash, snapshot, revoke), public `/share/{token}` page + API, noindex, rate limit |
| 16 | Web search | ❌ | — | search provider + tool calling, citations, per-message toggle (decision C) |
| 17 | Voice | ❌ | — | `POST /api/audio/transcribe` (Groq Whisper — available), mic recording, `speechSynthesis` read-aloud |
| 18 | Like/dislike | ❌ | — | `MessageFeedback`, `POST /api/messages/{id}/feedback`, thumbs + comment |
| 19 | Dark mode | 🟡 | Full dark theme via CSS variables, follows the OS; code blocks always dark | **Toggle** (light/dark/system) persisted in `UserSettings.Theme` |
| 20 | Full chat export | 🟡 | `GET /api/conversations/{id}/export` → **TXT only**; reply renderers exist for PDF/DOCX | `?format=pdf|docx|txt` reusing the renderers, UI format menu |
| 21 | Storage, cleanup, malware, background | 🟡 | `IFileStorage` + `LocalFileStorage`; private storage; GUID keys; downloads stream | Azure Blob / S3 implementations, hourly orphan cleanup, malware scan before "completed", background queue + polling (decisions E, F) |
| 22 | Admin, usage, quotas | ❌ | Rate limits only | `Users.Role/IsBlocked`, role in JWT, `UsageDaily`, quotas (429 with reset time), admin APIs + pages, first-admin bootstrap (decision G) |
| 23 | Local-timezone timestamps | 🟡 | Stored UTC; exports print UTC; UI shows no message times | Browser IANA zone sent on exports, `TimeZoneInfo` conversion, zone printed; local times in the UI |

Summary: **0 complete as specified, 8 partial (1, 2, 3, 10, 19, 20, 21, 23), 15 missing.**

## 4. Database changes (all additive, existing data preserved)

| Table | Change | Notes for existing data |
|---|---|---|
| Messages | + `ParentMessageId` (self-FK, NO ACTION), `VersionIndex`, `IsActive`, `IsTruncated`, `ModelId` | Migration back-fills `ParentMessageId` = previous message in the same conversation, `VersionIndex = 1`, `IsActive = 1` (36 rows) |
| Conversations | + `IsPinned`, `ArchivedAt`, `ModelId`, `ProjectId` (FK, SET NULL) | defaults `0` / `NULL` |
| ChunkEmbeddings (new) | `ChunkId` (PK/FK cascade), `Vector varbinary(max)`, `Model`, `Dimensions` | filled for existing chunks by a background job |
| DocumentChunks | + `Source` (`Text` / `OCR`) | default `Text` |
| UploadedFiles | + `ProgressCurrent/ProgressTotal` (OCR progress), `ScanStatus`; `Kind` image | defaults |
| MessageSources (new) | `MessageId`, `ChunkId`, `Score` | — |
| UserSettings (new) | `UserId` (PK/FK), `AboutMe` (1,500), `ResponseStyle` (1,500), `Theme` | created on first save |
| Projects, ProjectFiles (new) | owner, name, instructions; project ↔ file links | — |
| SharedLinks (new) | `ConversationId`, `TokenHash` (unique), `SnapshotJson`, `CreatedAt`, `RevokedAt` | — |
| MessageFeedback (new) | `MessageId`, `UserId`, `Rating`, `Comment`; unique (MessageId, UserId) | — |
| Users | + `Role` (default `User`), `IsBlocked` (default 0) | — |
| UsageDaily (new) | `UserId`, `Date`, `Messages`, `InputTokens`, `OutputTokens`, `Uploads`; unique (UserId, Date) | — |

One migration per phase (4 total), each with indexes for the new filters (e.g. `Conversations (UserId, ArchivedAt, IsPinned, UpdatedAt)`,
`Messages (ConversationId, IsActive)`). Optional later: full-text index on `Messages.Content` (Full-Text Search is installed).

## 5. Endpoints

| Roadmap endpoint | Today |
|---|---|
| POST /api/chat/{conversationId}/messages/stream | ✅ exists |
| POST /api/messages/{messageId}/regenerate | exists as `POST /api/chat/{id}/messages/regenerate` (replaces) → add the roadmap route with versions; keep the old one as an alias |
| PUT /api/messages/{messageId} | ❌ |
| GET /api/conversations/search?q= | ❌ |
| PATCH /api/conversations/{id} | ❌ (PUT rename exists and stays) |
| GET /api/files/{fileId}/chunks/{chunkId} | ❌ |
| GET / PUT /api/users/me/settings | ❌ |
| GET / POST / PUT / DELETE /api/projects | ❌ |
| POST / DELETE /api/conversations/{id}/share · GET /api/share/{token} | ❌ |
| POST /api/audio/transcribe | ❌ |
| POST /api/messages/{messageId}/feedback | ❌ |
| GET /api/conversations/{id}/export?format=pdf\|docx\|txt | TXT only (no `format`) |
| GET /api/admin/users · /api/admin/usage | ❌ |
| Extra, kept | `POST /api/chat/{id}/messages/stop`, `GET /api/messages/{id}/export`, `GET /api/files/{id}/download` |

## 6. Files to change / add, by phase

**Phase 1** — modify: `Models/Message.cs`, `Models/Conversation.cs`, `Data/Configurations/*`, `Services/ChatService.cs`,
`Services/ConversationService.cs`, `Repositories/ChatRepositories.cs`, `Interfaces/IChatRepositories.cs`,
`Controllers/ChatController.cs`, `Controllers/ConversationsController.cs`, `DTOs/Chat/ChatDtos.cs`, `Validators/ChatRequestValidators.cs`;
frontend `core/services/chat-api.service.ts`, `chat.store.ts`, `chat.models.ts`, `shared/components/chat-message`, `sidebar`,
`features/chat/chat-thread.*`, `chat-layout.ts`. Add: `Controllers/MessagesController.cs` actions (regenerate, edit),
`Services/ConversationSearchService.cs`, migration `Phase1ChatExperience`, frontend `core/services/sse-stream.ts` (fetch reader),
`shared/components/version-switcher`, tests `Integration/BranchingTests.cs`, `SearchAndPinTests.cs`, specs.

**Phase 2** — modify: `Services/Files/FileService.cs`, `FileSignature.cs`, `PdfExtractor.cs`, `DocumentContextBuilder.cs`,
`OpenAiChatProvider.cs` (image parts), `FilesController.cs`, `Program.cs`, `appsettings.json`; frontend `attachment-chip`,
`message-input`, `markdown` (citation chips), `file-api.service.ts`. Add: `Services/Embeddings/*` (provider + store + hybrid ranker),
`Services/Files/OcrService.cs`, `ImageProcessor.cs`, `CsvExtractor.cs`, `PptxExtractor.cs`, `Services/Files/DocumentProcessingQueue.cs`
(background worker, needed by OCR), `Models/ChunkEmbedding.cs`, `MessageSource.cs`, migration `Phase2DocumentIntelligence`,
frontend `shared/components/source-panel`, tests incl. the 10-pair retrieval set.

**Phase 3** — add: `Controllers/SettingsController` (or `UsersController` actions), `ProjectsController.cs`, `ShareController.cs`,
`AudioController.cs`, feedback action, `Services/Projects/*`, `Services/Sharing/*`, `Services/WebSearch/*`, `Services/Speech/*`,
models `UserSettings`, `Project`, `ProjectFile`, `SharedLink`, `MessageFeedback`, migration `Phase3PowerFeatures`;
frontend `features/projects`, `features/share` (public route), model selector, theme service + toggle, mic button,
read-aloud, feedback buttons, export menu; modify `ConversationExportService.cs`, `AiOptions.cs`, `profile` page.

**Phase 4** — add: `Services/Files/AzureBlobFileStorage.cs` / `S3FileStorage.cs`, `OrphanFileCleanupService.cs`,
`Services/Security/IMalwareScanner` (+ Defender / ClamAV implementations), `Services/Usage/*` (UsageDaily, quotas),
`Controllers/AdminController.cs`, models `UsageDaily`, migration `Phase4ProductionReadiness`; modify `TokenService.cs`
(role claim), auth checks for `IsBlocked`, `FileService.cs`; frontend `features/admin` (users, usage, feedback), guard,
local-time display and time-zone header.

## 7. Decisions needed before the affected features

| | Feature | Question | Recommendation |
|---|---|---|---|
| A | 7 Embeddings | Groq has no embedding model. | **Local ONNX model** (`multilingual-e5-small`, free, offline, supports Hindi) behind `Ai:EmbeddingProvider`, with OpenAI as a config option later. Alternative: add an OpenAI key (paid, small cost). |
| B | 8 OCR | Local or cloud OCR? | **Tesseract** (free, local; English + Hindi language data) + PDFium page rendering. Azure Document Intelligence needs a paid key. |
| C | 16 Web search | Which provider? (Bing Search API was retired in 2025.) | **Tavily** (free tier ~1,000 searches/month) or Brave Search; you create the key and store it with `dotnet user-secrets`. |
| D | 10 .doc | LibreOffice is not installed. | Reject `.doc` with a clear "save as .docx" message (the roadmap's fallback); convert only if LibreOffice is installed later. |
| E | 21 Malware scan | Which scanner? | Interface with **Windows Defender** (available now on this PC) for development and **ClamAV** (Docker) for Linux servers; tests use the harmless EICAR test file. |
| F | 21 Cloud storage | Azure Blob, S3, or both? Where will it be deployed? | Implement the one you will deploy to; test against its emulator in Docker (Azurite / MinIO). Local disk stays the default. |
| G | 22 Admin | How is the first admin chosen? | `Admin:Emails` setting: those accounts get the Admin role at sign-in. |
| H | — | Commit the existing uncommitted streaming work first? | Yes — one commit, then Phase 1. |

## 8. Plan

Phases in roadmap order, each ending with: build, all tests (old + new) green, a real browser run with the Groq model,
a short report (passed / failed / not done), docs updated, then your go-ahead for the next phase.

Phase 1 next steps: (1) switch the client to fetch + ReadableStream with AbortController and a 50 ms render throttle,
(2) `IsTruncated` + "(stopped)", (3) reply versions instead of replacement + `POST /api/messages/{id}/regenerate`,
(4) edit with full branching (not the simplified fallback), (5) search, (6) pin/archive — one migration for (2)–(6).
