import { useLayoutEffect, useRef, useState } from "react";

interface NumberFieldProps {
  value: number | null;
  onChange: (value: number | null) => void;
  /** Số chữ số thập phân được phép; 0 là chỉ số nguyên. */
  decimals?: number;
  /** Nhóm hàng nghìn bằng dấu chấm, ví dụ 150.000. */
  grouping?: boolean;
  suffix?: string;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
  "aria-label"?: string;
}

const MAX_INTEGER_DIGITS = 15;

function formatDraft(raw: string, decimals: number, grouping: boolean): string {
  // Khi không nhóm nghìn, dấu chấm cũng được hiểu là dấu thập phân
  const normalized = grouping ? raw.replace(/\./g, "") : raw.replace(/\./g, ",");
  const commaIndex = decimals > 0 ? normalized.indexOf(",") : -1;
  const integerRaw = (commaIndex >= 0 ? normalized.slice(0, commaIndex) : normalized).replace(/\D/g, "");
  const integer = integerRaw.replace(/^0+(?=\d)/, "").slice(0, MAX_INTEGER_DIGITS);
  const shownInteger = grouping ? integer.replace(/\B(?=(\d{3})+(?!\d))/g, ".") : integer;
  if (commaIndex < 0) return shownInteger;
  const fraction = normalized.slice(commaIndex + 1).replace(/\D/g, "").slice(0, decimals);
  return `${shownInteger || "0"},${fraction}`;
}

/** Vị trí con trỏ sau đúng số ký tự có nghĩa, bỏ qua dấu chấm nghìn. */
function caretPosition(draft: string, significant: number): number {
  let position = 0;
  let seen = 0;
  while (position < draft.length && seen < significant) {
    if (draft[position] !== ".") seen += 1;
    position += 1;
  }
  return position;
}

function parseDraft(draft: string): number | null {
  const compact = draft.replace(/\./g, "").replace(",", ".");
  if (compact === "" || compact === ".") return null;
  const parsed = Number(compact);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatValue(value: number | null, decimals: number, grouping: boolean): string {
  if (value === null || !Number.isFinite(value)) return "";
  return formatDraft(String(value).replace(".", ","), decimals, grouping);
}

/**
 * Ô nhập số dùng chung: xóa được hết, không tự chèn số 0, tự nhóm hàng nghìn và giữ đúng vị trí con trỏ.
 * Bỏ trống trả về null để nơi dùng phân biệt được "chưa nhập" với giá trị 0.
 */
export function NumberField({
  value,
  onChange,
  decimals = 0,
  grouping = true,
  suffix,
  placeholder,
  disabled,
  id,
  "aria-label": ariaLabel,
}: NumberFieldProps) {
  const [draft, setDraft] = useState(() => formatValue(value, decimals, grouping));
  const inputRef = useRef<HTMLInputElement>(null);
  const caretRef = useRef<number | null>(null);

  // Đồng bộ khi giá trị đổi từ bên ngoài, nhưng không ghi đè lúc đang gõ dở như "12,"
  const [lastValue, setLastValue] = useState(value);
  if (value !== lastValue) {
    setLastValue(value);
    if (value !== parseDraft(draft)) {
      setDraft(formatValue(value, decimals, grouping));
    }
  }

  // Giữ con trỏ sau đúng số ký tự có nghĩa khi dấu chấm nghìn thay đổi
  useLayoutEffect(() => {
    const input = inputRef.current;
    const significant = caretRef.current;
    if (!input || significant === null) return;
    caretRef.current = null;
    const position = caretPosition(draft, significant);
    input.setSelectionRange(position, position);
  }, [draft]);

  return (
    <div className="number-field">
      <input
        ref={inputRef}
        id={id}
        className="input number-field__input"
        type="text"
        inputMode={decimals > 0 ? "decimal" : "numeric"}
        autoComplete="off"
        aria-label={ariaLabel}
        placeholder={placeholder}
        disabled={disabled}
        value={draft}
        onFocus={(event) => event.target.select()}
        onChange={(event) => {
          const raw = event.target.value;
          const caret = event.target.selectionStart ?? raw.length;
          const beforeCaret = raw.slice(0, caret);
          const significant = (grouping ? beforeCaret.replace(/\./g, "") : beforeCaret).replace(/[^\d,.]/g, "").length;
          const next = formatDraft(raw, decimals, grouping);
          if (next === draft) {
            // Xóa đúng dấu chấm nghìn: số không đổi nên không render lại; tự giữ con trỏ để không nhảy về cuối
            const position = caretPosition(next, significant);
            event.target.value = next;
            event.target.setSelectionRange(position, position);
            return;
          }
          caretRef.current = significant;
          setDraft(next);
          const parsed = parseDraft(next);
          setLastValue(parsed);
          onChange(parsed);
        }}
      />
      {suffix && <span className="number-field__suffix">{suffix}</span>}
    </div>
  );
}
