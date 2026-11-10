export const AI_CHAT_SESSION_END = "oism_ai_chat_session_end";

export interface AiChatMessage {
  role: "user" | "assistant";
  content: string;
}

export interface AiChatRequest {
  messages: AiChatMessage[];
  branchId?: string;
  confirmBranchChoice?: boolean;
  model?: string;
}

export interface AiChatBranchChoice {
  id: string;
  name: string;
}

export interface AiChatResponse {
  reply: string;
  usedTools: string[];
  dateRange: { from: string; to: string } | null;
  scope: string | null;
  branchChoices: AiChatBranchChoice[];
  model: string | null;
}

export interface AiModelOption {
  id: string;
  label: string;
  isDefault: boolean;
}
