import { useEffect, useState } from "react";
import { Button, Modal } from "../ui";
import { unitService, parseUnitApiError } from "../../services/unitService";
import type { SkuUnit, SkuUnitFactorHistory } from "../../types/units";

interface SkuUnitHistoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  productId: string;
  variantId: string;
  skuUnit: SkuUnit | null;
  baseUnitSymbol?: string;
}

export function SkuUnitHistoryModal({
  isOpen,
  onClose,
  productId,
  variantId,
  skuUnit,
  baseUnitSymbol = "",
}: SkuUnitHistoryModalProps) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [history, setHistory] = useState<SkuUnitFactorHistory[]>([]);

  useEffect(() => {
    if (isOpen && skuUnit) {
      loadHistory();
    }
  }, [isOpen, skuUnit]);

  const loadHistory = async () => {
    if (!skuUnit) return;
    setLoading(true);
    setError(null);
    try {
      const data = await unitService.getSkuUnitHistory(productId, variantId, skuUnit.id);
      setHistory(data);
    } catch (err: any) {
      const parsed = parseUnitApiError(err);
      setError(parsed.detail || "Không thể tải lịch sử hệ số quy đổi.");
    } finally {
      setLoading(false);
    }
  };

  if (!isOpen || !skuUnit) return null;

  return (
    <Modal onClose={onClose} title={`Lịch sử hệ số quy đổi: ${skuUnit.unitName} (${skuUnit.unitCode})`}>
      <div className="space-y-4 text-sm text-slate-700">
        <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg flex items-center justify-between">
          <div>
            <span className="text-slate-500">Đơn vị quy đổi: </span>
            <span className="font-semibold text-slate-900">{skuUnit.unitName} ({skuUnit.unitSymbol})</span>
          </div>
          <div>
            <span className="text-slate-500">Hệ số hiện tại: </span>
            <span className="font-semibold text-indigo-600">
              1 {skuUnit.unitSymbol} = {skuUnit.factorToBase.toLocaleString("vi-VN")} {baseUnitSymbol}
            </span>
          </div>
        </div>

        {error && (
          <div className="p-3 bg-rose-50 border border-rose-200 text-rose-700 rounded-lg text-xs">
            {error}
          </div>
        )}

        {loading ? (
          <div className="py-8 text-center text-slate-400">Đang tải lịch sử...</div>
        ) : history.length === 0 ? (
          <div className="py-8 text-center text-slate-400">
            Chưa có biến động hệ số nào được ghi nhận.
          </div>
        ) : (
          <div className="border border-slate-200 rounded-lg overflow-hidden">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-100 text-slate-600 font-semibold border-b border-slate-200">
                <tr>
                  <th className="p-2.5">Phiên bản</th>
                  <th className="p-2.5">Hệ số cũ</th>
                  <th className="p-2.5">Hệ số mới</th>
                  <th className="p-2.5">Thời điểm áp dụng</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {history.map((h) => (
                  <tr key={h.id} className="hover:bg-slate-50">
                    <td className="p-2.5 font-medium">v{h.newVersion}</td>
                    <td className="p-2.5 text-slate-500">
                      {h.oldFactor != null ? `${Number(h.oldFactor).toLocaleString("vi-VN")} ${baseUnitSymbol}` : "—"}
                    </td>
                    <td className="p-2.5 font-semibold text-indigo-700">
                      {Number(h.newFactor).toLocaleString("vi-VN")} {baseUnitSymbol}
                    </td>
                    <td className="p-2.5 text-slate-600">
                      {new Date(h.changedAt).toLocaleString("vi-VN")}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="flex justify-end pt-2">
          <Button variant="secondary" onClick={onClose}>
            Đóng
          </Button>
        </div>
      </div>
    </Modal>
  );
}
