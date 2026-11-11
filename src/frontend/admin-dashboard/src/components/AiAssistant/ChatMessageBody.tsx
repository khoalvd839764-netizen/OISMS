import { Fragment } from "react";
import { parseChatMessage, type ChatLine } from "./chatFormat";

function MessageLine({ segments }: { segments: ChatLine }) {
  return segments.map(({ text, bold, code }, index) => {
    const content = code ? <code>{text}</code> : text;
    return <Fragment key={index}>{bold ? <strong>{content}</strong> : content}</Fragment>;
  });
}

export function ChatMessageBody({ content }: { content: string }) {
  return (
    <div className="ai-assistant__formatted">
      {parseChatMessage(content).map((block, index) => {
        if (block.type === "paragraph") {
          return (
            <p key={index}>
              {block.lines.map((line, lineIndex) => (
                <Fragment key={lineIndex}>
                  {lineIndex > 0 && <br />}
                  <MessageLine segments={line} />
                </Fragment>
              ))}
            </p>
          );
        }

        const List = block.type === "bullets" ? "ul" : "ol";
        return (
          <List key={index}>
            {block.items.map((line, lineIndex) => <li key={lineIndex}><MessageLine segments={line} /></li>)}
          </List>
        );
      })}
    </div>
  );
}
