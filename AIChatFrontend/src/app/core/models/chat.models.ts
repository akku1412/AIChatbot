/** Mirrors the ChatGPTClone.Api conversation/message DTOs (implemented in Phase 8). */

export interface ConversationSummary {
  conversationId: number;
  title: string;
  createdAt: string;
  updatedAt: string;
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
}

export interface ChatMessage {
  messageId: number;
  role: MessageRole;
  content: string;
  createdAt: string;
  tokenUsage: number | null;
  attachments: AttachmentInfo[];
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
  statusMessage: string | null;
  pageCount: number | null;
  extractedCharacters: number;
  createdAt: string;
}

/** GET /api/files/limits (mirrors the server's FileUploads settings). */
export interface UploadLimits {
  maxFileSizeBytes: number;
  maxFilesPerMessage: number;
  allowedExtensions: string[];
}

/** Used until /api/files/limits answers; the server enforces the real values anyway. */
export const DEFAULT_UPLOAD_LIMITS: UploadLimits = {
  maxFileSizeBytes: 20 * 1024 * 1024,
  maxFilesPerMessage: 5,
  allowedExtensions: ['.pdf', '.docx', '.xlsx', '.xls', '.txt'],
};

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
