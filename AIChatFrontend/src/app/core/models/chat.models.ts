/** Mirrors the ChatGPTClone.Api conversation/message DTOs (implemented in Phase 8). */

export interface ConversationSummary {
  conversationId: number;
  title: string;
  createdAt: string;
  updatedAt: string;
  isPinned: boolean;
  /** Set when the chat is archived (hidden from the main list). */
  archivedAt: string | null;
}

/** PATCH /api/conversations/{id}: only the fields present change. */
export interface ConversationPatch {
  title?: string;
  isPinned?: boolean;
  archived?: boolean;
}

/** GET /api/conversations/search?q= */
export interface ConversationSearchResult {
  conversationId: number;
  title: string;
  updatedAt: string;
  isPinned: boolean;
  archivedAt: string | null;
  titleMatches: boolean;
  /** Text around the match in the newest matching message, if any. */
  snippet: string | null;
  messageId: number | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
}

export type MessageRole = 'user' | 'assistant';

/** A file shown on a chat message. */
export interface AttachmentInfo {
  fileId: string;
  fileName: string;
  fileExtension: string;
  fileSize: number;
  /** PNG/JPEG/WebP: shown as a thumbnail and sent to the vision model. */
  isImage?: boolean;
}

export interface ChatMessage {
  messageId: number;
  role: MessageRole;
  content: string;
  createdAt: string;
  tokenUsage: number | null;
  attachments: AttachmentInfo[];
  /** The message this one follows (messages form a tree of versions). */
  parentMessageId: number | null;
  /** Position among the versions of this message, for the "‹ 2/3 ›" arrows. */
  versionNumber: number;
  versionCount: number;
  /** The user stopped this reply before it finished. */
  isTruncated: boolean;
  /** AI replies: the document passages it cited as [file:chunkId] (only real, owned passages; best match first). */
  sources?: MessageSource[];
}

/** A passage an AI reply cites. */
export interface MessageSource {
  chunkId: number;
  fileId: string;
  fileName: string;
  /** "Page 3", "Slide 4", "Sheet 'Q1'", … */
  location: string;
  /** The text was read from a scanned page or an image. */
  isOcr: boolean;
}

/** GET /api/files/{fileId}/chunks/{chunkId}: the cited passage for the preview panel. */
export interface ChunkPreview {
  chunkId: number;
  fileId: string;
  fileName: string;
  location: string;
  content: string;
  /** "text" or "ocr". */
  source: string;
  isSummary: boolean;
}

/** GET /api/messages/{id}/export?format=… */
export type ExportFormat = 'txt' | 'pdf' | 'docx' | 'xlsx';

export type FileStatus = 'uploaded' | 'processing' | 'completed' | 'failed';

/** POST /api/files/upload and GET /api/files/{id}. */
export interface UploadedFile {
  fileId: string;
  fileName: string;
  fileExtension: string;
  contentType: string;
  fileSize: number;
  status: FileStatus;
  /** While processing: the current step ("Reading scanned pages"); when failed: why. */
  statusMessage: string | null;
  /** Machine-readable failure reason ("timeout", "unsupported", …). */
  statusReason?: string | null;
  /** While processing, e.g. scanned pages read so far / in total. */
  progressCurrent?: number | null;
  progressTotal?: number | null;
  pageCount: number | null;
  extractedCharacters: number;
  isImage?: boolean;
  createdAt: string;
}

/** GET /api/files/limits (mirrors the server's FileUploads settings). */
export interface UploadLimits {
  maxFileSizeBytes: number;
  maxFilesPerMessage: number;
  allowedExtensions: string[];
  /** Images have their own, smaller size limit. */
  maxImageSizeBytes: number;
  imageExtensions: string[];
}

/** Used until /api/files/limits answers; the server enforces the real values anyway. */
export const DEFAULT_UPLOAD_LIMITS: UploadLimits = {
  maxFileSizeBytes: 20 * 1024 * 1024,
  maxFilesPerMessage: 5,
  allowedExtensions: ['.pdf', '.docx', '.xlsx', '.xls', '.csv', '.pptx', '.txt', '.png', '.jpg', '.jpeg', '.webp'],
  maxImageSizeBytes: 10 * 1024 * 1024,
  imageExtensions: ['.png', '.jpg', '.jpeg', '.webp'],
};

/** Same wording as the server's "unsupported file type" error. */
export const SUPPORTED_FILES_TEXT =
  'PDF, Word (.docx), Excel (.xlsx, .xls), CSV, PowerPoint (.pptx), text (.txt) and images (PNG, JPG, WebP)';

/** Word 97–2003 can't be read safely on the server; tell the user how to convert it (matches the server message). */
export const DOC_NOT_SUPPORTED_TEXT =
  "Word 97–2003 (.doc) files aren't supported. Open the file in Word, choose File → Save As → Word Document (.docx), and upload that.";

export interface MessagePage {
  items: ChatMessage[];
  hasMore: boolean;
}

/** POST /api/chat/{id}/messages returns both saved messages and the (possibly retitled) conversation. */
export interface SendMessageResponse {
  userMessage: ChatMessage;
  assistantMessage: ChatMessage;
  conversation: ConversationSummary;
}

/** The "done" event of a streamed reply (POST …/messages/stream and …/regenerate). */
export interface StreamTurnResult {
  userMessage: ChatMessage;
  /** Null when generation was stopped before any text. */
  assistantMessage: ChatMessage | null;
  conversation: ConversationSummary;
  stopped: boolean;
  truncated: boolean;
  /** The previous reply that a regeneration replaced. */
  replacedMessageId: number | null;
}

export type ChatStreamEvent = { type: 'delta'; text: string } | { type: 'done'; result: StreamTurnResult };

/** Longest message the UI and API accept. */
export const MAX_MESSAGE_LENGTH = 8000;
