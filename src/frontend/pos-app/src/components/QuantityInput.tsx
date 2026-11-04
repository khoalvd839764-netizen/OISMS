import { useEffect, useState } from 'react';

interface QuantityInputProps {
  value: number;
  max: number;
  decimalPlaces: number;
  label: string;
  className?: string;
  onCommit: (quantity: number) => void;
}

/** Ô số lượng giữ giá trị đang gõ để nhập được số lẻ như 0,5 và khôi phục số cũ nếu để trống. */
export function QuantityInput({ value, max, decimalPlaces, label, className, onCommit }: QuantityInputProps) {
  const [draft, setDraft] = useState(String(value));

  // Không ghi đè khi đang gõ dở một số có cùng giá trị, ví dụ "1." hoặc "0.50"
  useEffect(() => {
    setDraft((current) => (Number(current.replace(',', '.')) === value ? current : String(value)));
  }, [value]);

  const commit = () => {
    const parsed = Number(draft.replace(',', '.'));
    if (draft.trim() === '' || !Number.isFinite(parsed) || parsed <= 0) {
      setDraft(String(value));
      return;
    }
    onCommit(parsed);
  };

  return (
    <input
      type="number"
      min={decimalPlaces > 0 ? 1 / 10 ** decimalPlaces : 1}
      max={max}
      step={decimalPlaces > 0 ? 1 / 10 ** decimalPlaces : 1}
      aria-label={label}
      className={className}
      value={draft}
      onChange={(event) => {
        const next = event.target.value;
        setDraft(next);
        // Ghi ngay số hợp lệ để phím tắt thanh toán không dùng số lượng cũ khi chưa rời ô
        const parsed = Number(next.replace(',', '.'));
        if (next.trim() !== '' && Number.isFinite(parsed) && parsed > 0) onCommit(parsed);
      }}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          commit();
        }
      }}
    />
  );
}
