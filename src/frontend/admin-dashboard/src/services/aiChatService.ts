import axios, { type AxiosRequestConfig } from "axios";
import { httpClient } from "./httpClient";
import type { AiChatMessage, AiChatRequest, AiChatResponse, AiModelOption } from "../types/aiChat";

export type AiChatError = {
  kind: "disabled" | "rate-limited" | "model-quota" | "unavailable" | "invalid" | "unauthorized" | "forbidden";
  message: string;
};

type AiChatProblem = {
  errorCode?: string;
  detail?: string;
  message?: string;
  extensions?: { errorCode?: string };
};

export interface AiChatBranch {
  id: string;
  name: string;
  isActive: boolean;
}

export function trimHistory(messages: AiChatMessage[], limit = 10): AiChatMessage[] {
  return messages.slice(-limit).map(({ role, content }) => ({ role, content }));
}

export function buildRequest(
  messages: AiChatMessage[], branchId?: string, confirmBranchChoice = false, model?: string,
): AiChatRequest {
  return {
    messages: trimHistory(messages),
    ...(branchId ? { branchId } : {}),
    ...(confirmBranchChoice ? { confirmBranchChoice } : {}),
    ...(model ? { model } : {}),
  };
}

export function mapAiChatError(status?: number, errorCode?: string, detail?: string): AiChatError {
  if (status === 401) {
    return { kind: "unauthorized", message: "" };
  }
  if (errorCode === "AI_DISABLED") {
    return { kind: "disabled", message: "Trợ lý AI chưa được bật." };
  }
  // Model đã chọn hết lượt ở nhà cung cấp: người dùng vẫn hỏi được bằng model khác, không khóa gửi.
  if (errorCode === "AI_MODEL_QUOTA") {
    return { kind: "model-quota", message: detail || "Model AI đã chọn đang hết lượt. Chọn model khác hoặc thử lại sau." };
  }
  // Giới hạn 10 câu/phút của OISMS cho mỗi người dùng.
  if (status === 429 || errorCode === "AI_RATE_LIMITED") {
    return { kind: "rate-limited", message: "Bạn hỏi hơi nhanh, thử lại sau ít phút." };
  }
  if (status === 400) {
    return { kind: "invalid", message: detail || "Yêu cầu trợ lý AI không hợp lệ." };
  }
  if (status === 403) {
    return { kind: "forbidden", message: "Bạn không có quyền dùng Trợ lý OISMS." };
  }
  return { kind: "unavailable", message: "Trợ lý AI tạm thời không trả lời được." };
}

class AiChatRequestError extends Error {
  readonly problem: AiChatError;

  constructor(problem: AiChatError) {
    super(problem.message);
    this.problem = problem;
  }
}

export function readAiChatError(error: unknown): AiChatError {
  if (error instanceof AiChatRequestError) {
    return error.problem;
  }
  if (axios.isAxiosError<AiChatProblem>(error)) {
    const { data, status } = error.response ?? {};
    return mapAiChatError(status, data?.errorCode ?? data?.extensions?.errorCode, data?.detail ?? data?.message);
  }
  return mapAiChatError();
}

function panelRequestConfig(signal: AbortSignal): AxiosRequestConfig {
  return {
    // Keep the shared JWT refresh for 401. Other failures belong to the panel.
    validateStatus: (status) => status !== 401,
    adapter: async (config) => {
      // Give the signal to the transport so cancel and offline failures do not reach the shared network banner.
      const transport = axios.getAdapter(httpClient.defaults.adapter);
      try {
        if (!signal.aborted) {
          return await transport({ ...config, signal });
        }
      } catch (error) {
        if (!axios.isAxiosError(error) || error.response) throw error;
      }
      return { config, data: undefined, status: 0, statusText: "", headers: {} };
    },
  };
}

function readPanelResponse<T>(status: number, data: T | AiChatProblem): T {
  if (status >= 200 && status < 300) {
    return data as T;
  }
  const problem = data as AiChatProblem | undefined;
  throw new AiChatRequestError(mapAiChatError(
    status, problem?.errorCode ?? problem?.extensions?.errorCode, problem?.detail ?? problem?.message,
  ));
}

export async function sendChat(request: AiChatRequest, signal: AbortSignal): Promise<AiChatResponse> {
  const { status, data } = await httpClient.post<AiChatResponse | AiChatProblem>("/api/ai/chat", request, {
    ...panelRequestConfig(signal),
    // Một câu hỏi có thể gọi model 2–4 lượt; Gemini mỗi lượt có thể mất 15–20 giây.
    timeout: 90_000,
  });
  const result = readPanelResponse<AiChatResponse>(status, data);
  return { ...result, scope: result.scope ?? null, branchChoices: result.branchChoices ?? [], model: result.model ?? null, sources: result.sources ?? [] };
}

export async function loadAiModels(signal: AbortSignal): Promise<AiModelOption[]> {
  const { status, data } = await httpClient.get<AiModelOption[] | AiChatProblem>("/api/ai/models", panelRequestConfig(signal));
  return readPanelResponse<AiModelOption[]>(status, data);
}

export async function loadAiChatBranches(signal: AbortSignal): Promise<AiChatBranch[]> {
  const { status, data } = await httpClient.get<AiChatBranch[] | AiChatProblem>("/api/branches", panelRequestConfig(signal));
  return readPanelResponse<AiChatBranch[]>(status, data).filter((branch) => branch.isActive);
}
