import { Bot } from "lucide-react";
import { Button } from "../ui";

export function AiAssistantButton({ open, onClick }: { open: boolean; onClick: () => void }) {
  return (
    <Button
      variant="ghost"
      className="ai-assistant__toggle"
      aria-label="Trợ lý OISMS"
      aria-expanded={open}
      aria-controls={open ? "oisms-ai-assistant" : undefined}
      title="Trợ lý OISMS"
      onClick={onClick}
    >
      <Bot size={20} aria-hidden="true" />
    </Button>
  );
}
