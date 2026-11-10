import { useCallback, useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { AI_CHAT_SESSION_END } from "../../types/aiChat";
import { AiAssistantButton } from "./AiAssistantButton";
import { AiAssistantPanel } from "./AiAssistantPanel";
import { useAiChat } from "./useAiChat";

export function AiAssistant() {
  const [open, setOpen] = useState(false);
  const [branchId, setBranchId] = useState("");
  const [sessionEnded, setSessionEnded] = useState(false);
  const chat = useAiChat(() => setOpen(false));
  const { stop, reset } = chat;

  useEffect(() => {
    function endSession() {
      reset();
      setBranchId("");
      setOpen(false);
      setSessionEnded(true);
    }
    window.addEventListener(AI_CHAT_SESSION_END, endSession);
    return () => window.removeEventListener(AI_CHAT_SESSION_END, endSession);
  }, [reset]);

  const close = useCallback(() => {
    stop();
    setOpen(false);
  }, [stop]);

  if (sessionEnded) return null;
  return (
    <>
      {chat.status !== "disabled" && (
        <AiAssistantButton open={open} onClick={() => open ? close() : setOpen(true)} />
      )}
      {open && createPortal(
        <AiAssistantPanel chat={chat} branchId={branchId} onBranchChange={setBranchId} onClose={close} />,
        document.body,
      )}
    </>
  );
}
