export type ChatSegment = { text: string; bold?: boolean; code?: boolean };
export type ChatLine = ChatSegment[];
export type ChatBlock =
  | { type: "paragraph"; lines: ChatLine[] }
  | { type: "bullets" | "numbers"; items: ChatLine[] };

function parseLine(line: string): ChatLine {
  const segments: ChatLine = [];
  let offset = 0;

  for (const match of line.matchAll(/\x60([^\x60]+)\x60|\*\*((?:[^*]|\*(?!\*))+?)\*\*|\*\*/g)) {
    if (match.index > offset) segments.push({ text: line.slice(offset, match.index) });
    if (match[1] !== undefined) segments.push({ text: match[1], code: true });
    if (match[2] !== undefined) segments.push({ text: match[2], bold: true });
    offset = match.index + match[0].length;
  }

  if (offset < line.length) segments.push({ text: line.slice(offset) });
  return segments;
}

export function parseChatMessage(text: string): ChatBlock[] {
  const blocks: ChatBlock[] = [];
  let current: ChatBlock | undefined;

  for (const rawLine of text.split(/\r\n?|\n/)) {
    const line = rawLine.trim();
    if (!line) {
      current = undefined;
      continue;
    }

    if (/^\|(?:\s*:?-{3,}:?\s*\|)+$/.test(line)) continue;

    const bullet = /^[-*•]\s+(.+)$/.exec(line);
    const number = /^\d+\.\s+(.+)$/.exec(line);
    const heading = /^#{1,6}\s*(.+)$/.exec(line);
    const isTable = line.startsWith("|") && line.endsWith("|");
    const body = isTable
      ? line.slice(1, -1).split("|").map((cell) => cell.trim()).join(" · ")
      : bullet?.[1] ?? number?.[1] ?? heading?.[1] ?? line;
    const segments = parseLine(body);
    if (!segments.length) continue;

    if (heading) {
      blocks.push({ type: "paragraph", lines: [segments.map((segment) => ({ ...segment, bold: true }))] });
      current = undefined;
    } else if (bullet || number) {
      const type = bullet ? "bullets" : "numbers";
      if (current?.type !== type) {
        current = { type, items: [] };
        blocks.push(current);
      }
      current.items.push(segments);
    } else {
      if (current?.type !== "paragraph") {
        current = { type: "paragraph", lines: [] };
        blocks.push(current);
      }
      current.lines.push(segments);
    }
  }

  return blocks;
}
