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

export interface ChatMessage {
  messageId: number;
  role: MessageRole;
  content: string;
  createdAt: string;
  tokenUsage: number | null;
}

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

/** Longest message the UI and API accept. */
export const MAX_MESSAGE_LENGTH = 8000;
