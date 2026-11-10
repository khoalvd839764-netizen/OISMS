import { useCallback, useEffect, useRef, useState } from "react";
import { buildRequest, readAiChatError, sendChat, type AiChatError } from "../../services/aiChatService";
import type { AiChatMessage, AiChatRequest, AiChatResponse } from "../../types/aiChat";

export type ChatEntry = AiChatMessage & {
  source?: Pick<AiChatResponse, "usedTools" | "dateRange" | "scope" | "model">;
  branchChoices?: AiChatResponse["branchChoices"];
};
type ChatStatus = "idle" | "sending" | "error" | "disabled";

export function useAiChat(onUnauthorized: () => void) {
  const [messages, setMessages] = useState<ChatEntry[]>([]);
  const [status, setStatus] = useState<ChatStatus>("idle");
  const [error, setError] = useState<AiChatError | null>(null);
  const [lastRequest, setLastRequest] = useState<AiChatRequest | null>(null);
  const [rateLimited, setRateLimited] = useState(false);
  const active = useRef<AbortController | null>(null);
  const cooldown = useRef<number | null>(null);

  useEffect(() => () => {
    active.current?.abort();
    active.current = null;
    if (cooldown.current !== null) window.clearTimeout(cooldown.current);
  }, []);

  const stop = useCallback(() => {
    active.current?.abort();
    active.current = null;
    setStatus((current) => current === "sending" ? "idle" : current);
  }, []);

  const reset = useCallback(() => {
    stop();
    setMessages([]);
    setLastRequest(null);
    setError(null);
    setStatus((current) => current === "disabled" ? current : "idle");
  }, [stop]);

  async function run(request: AiChatRequest) {
    const controller = new AbortController();
    active.current = controller;
    setLastRequest(request);
    setError(null);
    setStatus("sending");
    try {
      const { reply, usedTools, dateRange, scope, branchChoices, model } = await sendChat(request, controller.signal);
      if (active.current !== controller) return;
      setMessages((previous) => [...previous, {
        role: "assistant",
        content: reply,
        source: { usedTools, dateRange, scope, model },
        branchChoices,
      }]);
      setLastRequest(null);
      setStatus("idle");
    } catch (failure) {
      if (active.current !== controller) return;
      const issue = readAiChatError(failure);
      if (issue.kind === "unauthorized") {
        reset();
        onUnauthorized();
        return;
      }
      if (issue.kind === "rate-limited") {
        setRateLimited(true);
        cooldown.current = window.setTimeout(() => {
          cooldown.current = null;
          setRateLimited(false);
        }, 30_000);
      }
      setError(issue);
      setStatus(issue.kind === "disabled" ? "disabled" : "error");
    } finally {
      if (active.current === controller) active.current = null;
    }
  }

  const blocked = status === "sending" || status === "disabled" || rateLimited;

  function send(text: string, branchId?: string, model?: string): boolean {
    const content = text.trim();
    if (active.current || blocked || !content || content.length > 1000) return false;
    const history: ChatEntry[] = [...messages, { role: "user", content }];
    setMessages(history);
    void run(buildRequest(history, branchId, false, model));
    return true;
  }

  /** Gửi lại câu hỏi vừa lỗi; truyền model để hỏi lại bằng model khác. */
  function retry(model?: string) {
    if (!active.current && !blocked && lastRequest) {
      void run(model ? { ...lastRequest, model } : lastRequest);
    }
  }

  function chooseBranch(branchId: string, model?: string): boolean {
    if (active.current || blocked) return false;
    const lastUserIndex = messages.map((message) => message.role).lastIndexOf("user");
    if (lastUserIndex < 0) return false;
    void run(buildRequest(messages.slice(0, lastUserIndex + 1), branchId, true, model));
    return true;
  }

  return { messages, status, error, blocked, send, retry, reset, stop, chooseBranch };
}
