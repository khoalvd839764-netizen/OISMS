import { useEffect, useRef, useState } from "react";
import { Bot, MessageSquarePlus, Send, X } from "lucide-react";
import { Button } from "../ui";
import { loadAiChatBranches, loadAiModels, readAiChatError, type AiChatBranch } from "../../services/aiChatService";
import type { ChatEntry, useAiChat } from "./useAiChat";
import type { AiChatBranchChoice, AiModelOption } from "../../types/aiChat";
import { ChatMessageBody } from "./ChatMessageBody";

const QUESTIONS = [
  "Hôm nay nhập xuất bao nhiêu?",
  "Doanh thu 7 ngày qua?",
  "Nên nhập thêm gì tuần này?",
  "Cách tạo phiếu nhập hàng?",
];

const TODAY_SUMMARY = "Tóm tắt hôm nay về doanh thu, nhập/xuất kho, hàng sắp hết và gợi ý nhập hàng.";

const SOURCE_LABELS: Record<string, string> = {
  get_stock_movement: "Nhập/xuất kho",
  get_revenue: "Doanh thu",
  get_low_stock: "Cảnh báo tồn",
  get_reorder_suggestions: "Gợi ý nhập",
};

function shortDate(isoDate: string): string {
  const [, month, day] = isoDate.split("-");
  return day && month ? `${day}/${month}` : isoDate;
}

// Chỉ lưu lựa chọn model của người xem trên trình duyệt này; lịch sử chat không bao giờ được lưu.
const MODEL_STORAGE_KEY = "oism-ai-model";

function readSavedModel(): string {
  try {
    return window.localStorage.getItem(MODEL_STORAGE_KEY) ?? "";
  } catch {
    return "";
  }
}

function saveModel(model: string) {
  try {
    window.localStorage.setItem(MODEL_STORAGE_KEY, model);
  } catch {
    // Trình duyệt chặn storage thì chỉ giữ lựa chọn trong phiên hiện tại.
  }
}

function sourceLabel(source: ChatEntry["source"] | undefined, models: AiModelOption[]): string {
  if (!source) return "";
  const names = source.usedTools.map((tool) => SOURCE_LABELS[tool] ?? tool).join(", ");
  const range = source.dateRange;
  const modelLabel = source.model ? models.find((model) => model.id === source.model)?.label ?? source.model : "";
  const data = [names, source.scope, range && `${shortDate(range.from)}–${shortDate(range.to)}`].filter(Boolean);
  const parts = [data.length ? `Nguồn: ${data.join(" · ")}` : "", modelLabel && `Model: ${modelLabel}`].filter(Boolean);
  return parts.join(" · ");
}

type PanelProps = {
  chat: ReturnType<typeof useAiChat>;
  branchId: string;
  onBranchChange: (branchId: string) => void;
  onClose: () => void;
};

export function AiAssistantPanel({ chat, branchId, onBranchChange, onClose }: PanelProps) {
  const [branches, setBranches] = useState<AiChatBranch[]>([]);
  const [branchError, setBranchError] = useState(false);
  const [draft, setDraft] = useState("");
  const [models, setModels] = useState<AiModelOption[]>([]);
  const [model, setModel] = useState(readSavedModel);
  const input = useRef<HTMLTextAreaElement>(null);
  const transcript = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    loadAiChatBranches(controller.signal).then((items) => {
      if (!controller.signal.aborted) setBranches(items);
    }).catch((error) => {
      if (controller.signal.aborted) return;
      if (readAiChatError(error).kind === "unauthorized") onClose();
      else setBranchError(true);
    });
    return () => controller.abort();
  }, [onClose]);

  useEffect(() => {
    const controller = new AbortController();
    loadAiModels(controller.signal).then((items) => {
      if (controller.signal.aborted) return;
      setModels(items);
      // Lựa chọn đã lưu không còn khả dụng thì quay về model mặc định của hệ thống.
      setModel((current) => items.some((item) => item.id === current)
        ? current
        : items.find((item) => item.isDefault)?.id ?? items[0]?.id ?? "");
    }).catch(() => {
      // Không tải được danh sách thì backend tự dùng model mặc định.
    });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    input.current?.focus();
  }, []);

  const otherModel = models.find((option) => option.id !== model);

  function changeModel(next: string) {
    setModel(next);
    saveModel(next);
  }

  useEffect(() => {
    const list = transcript.current;
    if (list) list.scrollTop = list.scrollHeight;
  }, [chat.messages, chat.status]);

  function submit(text: string) {
    if (chat.send(text, branchId, model || undefined)) setDraft("");
  }

  function chooseBranch(branch: AiChatBranchChoice) {
    if (chat.chooseBranch(branch.id, model || undefined)) {
      setBranches((current) => current.some((item) => item.id === branch.id)
        ? current
        : [...current, { ...branch, isActive: true }]);
      onBranchChange(branch.id);
    }
  }

  return (
    <aside
      className="ai-assistant"
      id="oisms-ai-assistant"
      aria-labelledby="oisms-ai-assistant-title"
      onKeyDown={(event) => { if (event.key === "Escape") onClose(); }}
    >
      <header className="ai-assistant__header">
        <Bot size={21} aria-hidden="true" />
        <h2 id="oisms-ai-assistant-title">Trợ lý OISMS</h2>
        <Button
          variant="ghost"
          aria-label="Cuộc trò chuyện mới"
          title="Cuộc trò chuyện mới"
          disabled={chat.status === "disabled"}
          onClick={() => {
            chat.reset();
            setDraft("");
            input.current?.focus();
          }}
        >
          <MessageSquarePlus size={18} aria-hidden="true" />
        </Button>
        <Button variant="ghost" aria-label="Đóng trợ lý" title="Đóng trợ lý" onClick={onClose}>
          <X size={19} aria-hidden="true" />
        </Button>
      </header>

      <div className="ai-assistant__scope">
        <label htmlFor="oisms-ai-branch">Chi nhánh</label>
        <select id="oisms-ai-branch" value={branchId} onChange={(event) => onBranchChange(event.target.value)}>
          <option value="">Tất cả chi nhánh</option>
          {branches.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
        </select>
        {branchError && <small role="status">Không tải được danh sách chi nhánh.</small>}
        {models.length > 0 && (
          <>
            <label htmlFor="oisms-ai-model">Model AI</label>
            <select
              id="oisms-ai-model"
              value={model}
              disabled={models.length < 2 || chat.status === "sending"}
              onChange={(event) => changeModel(event.target.value)}
            >
              {models.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}
            </select>
          </>
        )}
      </div>

      <div className="ai-assistant__transcript" ref={transcript} role="log" aria-label="Tin nhắn trợ lý">
        {chat.messages.length === 0 && (
          <div className="ai-assistant__suggestions">
            <p>Bạn muốn xem số liệu hay tìm hướng dẫn sử dụng?</p>
            {QUESTIONS.map((question) => (
              <Button key={question} variant="secondary" disabled={chat.blocked} onClick={() => submit(question)}>
                {question}
              </Button>
            ))}
          </div>
        )}
        {chat.messages.map((message, index) => {
          const source = sourceLabel(message.source, models);
          return (
            <article key={index} className={`ai-assistant__message ai-assistant__message--${message.role}`}>
              <span className="ai-assistant__speaker">{message.role === "user" ? "Bạn" : "Trợ lý OISMS"}</span>
              {message.role === "assistant" ? <ChatMessageBody content={message.content} /> : <p>{message.content}</p>}
              {source && <small>{source}</small>}
              {!!message.branchChoices?.length && (
                <div className="ai-assistant__branch-choices" role="group" aria-label="Chọn chi nhánh">
                  {message.branchChoices.map((branch) => (
                    <Button
                      key={branch.id}
                      type="button"
                      variant="secondary"
                      disabled={chat.blocked || index !== chat.messages.length - 1}
                      onClick={() => chooseBranch(branch)}
                    >
                      {branch.name}
                    </Button>
                  ))}
                </div>
              )}
            </article>
          );
        })}
        {chat.status === "sending" && <p role="status" className="ai-assistant__waiting">Đang trả lời<span>…</span></p>}
      </div>

      {chat.error && (
        <div className="ai-assistant__error" role="alert">
          <span>
            {chat.error.message}
            {models.length > 1 && chat.error.kind === "unavailable" && " Bạn có thể chọn model AI khác ở trên."}
          </span>
          {chat.error.kind === "unavailable" && (
            <Button variant="ghost" onClick={() => chat.retry()} disabled={chat.blocked}>Thử lại</Button>
          )}
          {(chat.error.kind === "unavailable" || chat.error.kind === "model-quota") && otherModel && (
            <Button
              variant="ghost"
              disabled={chat.blocked}
              onClick={() => {
                changeModel(otherModel.id);
                chat.retry(otherModel.id);
              }}
            >
              Hỏi lại bằng {otherModel.label}
            </Button>
          )}
        </div>
      )}

      <form className="ai-assistant__composer" onSubmit={(event) => { event.preventDefault(); submit(draft); }}>
        <Button type="button" variant="secondary" disabled={chat.blocked} onClick={() => submit(TODAY_SUMMARY)}>
          Tóm tắt hôm nay
        </Button>
        <textarea
          aria-label="Câu hỏi cho Trợ lý OISMS"
          ref={input}
          rows={2}
          placeholder="Nhập câu hỏi…"
          value={draft}
          maxLength={1000}
          disabled={chat.status === "disabled"}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              submit(draft);
            }
          }}
        />
        <div className="ai-assistant__submit">
          <small>{draft.length}/1000</small>
          <Button type="submit" disabled={chat.blocked || !draft.trim()}>
            <Send size={15} aria-hidden="true" /> Gửi
          </Button>
        </div>
      </form>
    </aside>
  );
}
