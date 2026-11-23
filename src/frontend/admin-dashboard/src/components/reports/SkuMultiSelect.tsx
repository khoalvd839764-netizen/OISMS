import React, { useMemo, useState, useRef, useEffect } from 'react';
import { Search, X, ChevronDown } from 'lucide-react';

export interface SkuOption {
  id: string;
  code: string;
  name: string;
  variant?: string;
}

interface SkuMultiSelectProps {
  options: SkuOption[];
  selectedIds: string[];
  onChange: (nextIds: string[]) => void;
  maxSelect?: number;
}

export const SkuMultiSelect: React.FC<SkuMultiSelectProps> = ({
  options,
  selectedIds,
  onChange,
  maxSelect = 20,
}) => {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [limitNotice, setLimitNotice] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const optionMap = useMemo(() => {
    const m = new Map<string, SkuOption>();
    for (const o of options) m.set(o.id, o);
    return m;
  }, [options]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options.slice(0, 60);
    return options
      .filter(
        (o) =>
          o.code.toLowerCase().includes(q) ||
          o.name.toLowerCase().includes(q) ||
          (o.variant && o.variant.toLowerCase().includes(q))
      )
      .slice(0, 60);
  }, [options, query]);

  const toggleSku = (id: string) => {
    setLimitNotice(null);
    if (selectedIds.includes(id)) {
      onChange(selectedIds.filter((x) => x !== id));
      return;
    }
    if (selectedIds.length >= maxSelect) {
      setLimitNotice(`Chỉ được chọn tối đa ${maxSelect} SKU mỗi lần lọc.`);
      return;
    }
    onChange([...selectedIds, id]);
  };

  const clearAll = () => {
    setLimitNotice(null);
    onChange([]);
  };

  return (
    <div ref={containerRef} className="relative flex flex-col gap-1.5 min-w-[260px]" data-testid="sku-multi-select">
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white hover:bg-gray-50 flex items-center justify-between gap-2 text-xs text-gray-700 min-w-[220px]"
        >
          <span className="truncate">
            {selectedIds.length === 0
              ? 'Tất cả SKU (lọc theo SKU...)'
              : `Đã chọn ${selectedIds.length}/${maxSelect} SKU`}
          </span>
          <ChevronDown className="w-3.5 h-3.5 text-gray-400 flex-shrink-0" />
        </button>

        {selectedIds.length > 0 && (
          <button
            type="button"
            onClick={clearAll}
            className="px-2 py-1.5 rounded-lg border border-gray-200 bg-gray-50 hover:bg-gray-100 text-[11px] font-medium text-gray-600 flex items-center gap-1"
            title="Bỏ chọn tất cả SKU"
          >
            <X className="w-3 h-3" />
            Bỏ lọc SKU
          </button>
        )}
      </div>

      {open && (
        <div className="absolute top-full left-0 mt-1 w-80 bg-white border border-gray-200 rounded-xl shadow-lg z-30 p-2 space-y-2">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-gray-400 absolute left-2.5 top-2" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Tìm mã SKU hoặc tên sản phẩm..."
              className="w-full pl-8 pr-2.5 py-1.5 text-xs border border-gray-200 rounded-lg outline-none focus:ring-1 focus:ring-indigo-500"
              autoFocus
            />
          </div>

          {limitNotice && (
            <div className="px-2 py-1 rounded bg-amber-50 border border-amber-200 text-[11px] text-amber-800">
              {limitNotice}
            </div>
          )}

          <div className="max-h-56 overflow-y-auto divide-y divide-gray-100">
            {filtered.length === 0 ? (
              <div className="py-4 text-center text-[11px] text-gray-400">Không tìm thấy SKU phù hợp</div>
            ) : (
              filtered.map((o) => {
                const checked = selectedIds.includes(o.id);
                return (
                  <label
                    key={o.id}
                    className="flex items-center gap-2 px-2 py-1.5 hover:bg-indigo-50/50 cursor-pointer text-xs"
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleSku(o.id)}
                      className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    <div className="min-w-0 flex-1">
                      <div className="font-mono font-semibold text-gray-900 truncate">{o.code}</div>
                      <div className="text-[11px] text-gray-500 truncate">
                        {o.name}
                        {o.variant ? ` (${o.variant})` : ''}
                      </div>
                    </div>
                  </label>
                );
              })
            )}
          </div>
        </div>
      )}

      {selectedIds.length > 0 && (
        <div className="flex flex-wrap gap-1.5 max-w-xl">
          {selectedIds.map((id) => {
            const opt = optionMap.get(id);
            const label = opt ? `${opt.code}${opt.name ? ` · ${opt.name}` : ''}` : id.slice(0, 8);
            return (
              <span
                key={id}
                className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-indigo-50 text-indigo-700 border border-indigo-200"
              >
                <span className="truncate max-w-[160px]">{label}</span>
                <button
                  type="button"
                  onClick={() => toggleSku(id)}
                  className="hover:text-indigo-950"
                  aria-label={`Bỏ ${label}`}
                >
                  <X className="w-3 h-3" />
                </button>
              </span>
            );
          })}
        </div>
      )}
    </div>
  );
};
