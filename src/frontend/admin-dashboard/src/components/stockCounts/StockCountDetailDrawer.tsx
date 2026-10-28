import React, { useState, useEffect, useRef } from 'react';
import type { StockCount } from '../../types/stockCount';
import {
  STOCK_COUNT_STATUS_LABEL,
  STOCK_COUNT_STATUS_COLOR,
} from '../../types/stockCount';
import { stockCountService, type ProblemDetailsError } from '../../services/stockCountService';
import {
  X,
  AlertTriangle,
  CheckCircle2,
  Save,
  Ban,
  Info,
} from 'lucide-react';

interface StockCountDetailDrawerProps {
  countId: string | null;
  onClose: () => void;
  onRefresh: () => void;
}

export const StockCountDetailDrawer: React.FC<StockCountDetailDrawerProps> = ({
  countId,
  onClose,
  onRefresh,
}) => {
  const [detail, setDetail] = useState<StockCount | null>(null);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);

  // Form state đếm thực tế của từng SKU
  const [countedMap, setCountedMap] = useState<Record<string, string>>({});
  const [costMap, setCostMap] = useState<Record<string, string>>({});

  // Cảnh báo & Lỗi ProblemDetails
  const [errorDetails, setErrorDetails] = useState<ProblemDetailsError | null>(null);
  const [sequenceDrift, setSequenceDrift] = useState(false);
  const activeReqRef = useRef(0);

  useEffect(() => {
    if (!countId) {
      setDetail(null);
      return;
    }

    const reqSeq = ++activeReqRef.current;
    setDetail(null);
    setLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    const fetchDetail = async () => {
      try {
        const data = await stockCountService.getStockCountById(countId);
        if (reqSeq !== activeReqRef.current) return;
        setDetail(data);

        // Khởi tạo map giá trị đếm
        const initialCounted: Record<string, string> = {};
        const initialCost: Record<string, string> = {};
        data.items.forEach((item) => {
          if (item.countedQuantity !== undefined && item.countedQuantity !== null) {
            initialCounted[item.skuId] = String(item.countedQuantity);
          }
          if (item.adjustmentUnitCost !== undefined && item.adjustmentUnitCost !== null) {
            initialCost[item.skuId] = String(item.adjustmentUnitCost);
          }
        });
        setCountedMap(initialCounted);
        setCostMap(initialCost);
      } catch (err: any) {
        if (reqSeq !== activeReqRef.current) return;
        setErrorDetails({
          title: 'Không thể tải chi tiết đợt kiểm kê',
          detail: err?.message || 'Lỗi kết nối máy chủ.',
        });
      } finally {
        if (reqSeq === activeReqRef.current) {
          setLoading(false);
        }
      }
    };

    fetchDetail();
  }, [countId]);

  if (!countId) return null;

  const isEditable = detail?.status === 'Draft' || detail?.status === 'Counting';

  // Lưu bản đếm thực tế
  const handleSaveCounts = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;
    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    try {
      const itemsToRecord = detail.items.map((item) => {
        const valStr = countedMap[item.skuId];
        const countedVal = valStr !== undefined && valStr.trim() !== '' ? Number(valStr) : item.expectedQuantity;
        const costStr = costMap[item.skuId];
        const costVal = costStr && Number(costStr) > 0 ? Number(costStr) : undefined;

        return {
          skuId: item.skuId,
          countedQuantity: countedVal,
          adjustmentUnitCost: costVal,
        };
      });

      const updated = await stockCountService.recordCounts(targetId, {
        items: itemsToRecord,
      });

      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      setDetail(updated);
      onRefresh();
      alert('Đã lưu kết quả kiểm đếm thành công!');
    } catch (err: any) {
      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      setErrorDetails(err);
    } finally {
      if (actionSeq === activeReqRef.current && countId === targetId) {
        setActionLoading(false);
      }
    }
  };

  // Chốt kiểm kê & Cân bằng tồn
  const handleConfirmCount = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    // Kiểm tra dirty: Người dùng đã sửa số lượng hoặc đơn giá so với bản ghi đã lưu trên server chưa?
    const hasDirtyEdits = detail.items.some((item) => {
      const currentCountStr = countedMap[item.skuId];
      const savedCountStr =
        item.countedQuantity !== undefined && item.countedQuantity !== null
          ? String(item.countedQuantity)
          : '';
      if (
        currentCountStr !== undefined &&
        currentCountStr.trim() !== '' &&
        currentCountStr.trim() !== savedCountStr.trim()
      ) {
        return true;
      }

      const currentCostStr = costMap[item.skuId];
      const savedCostStr =
        item.adjustmentUnitCost !== undefined && item.adjustmentUnitCost !== null
          ? String(item.adjustmentUnitCost)
          : '';
      if (
        currentCostStr !== undefined &&
        currentCostStr.trim() !== '' &&
        currentCostStr.trim() !== savedCostStr.trim()
      ) {
        return true;
      }
      return false;
    });

    if (hasDirtyEdits) {
      alert("Bạn có thay đổi số lượng đếm hoặc giá vốn chưa được lưu. Vui lòng bấm 'Lưu kết quả đếm' trước khi Chốt kiểm kê.");
      return;
    }

    // Kiểm tra đã nhập hết chưa
    const uncounted = detail.items.filter(
      (item) => countedMap[item.skuId] === undefined || countedMap[item.skuId].trim() === ''
    );
    if (uncounted.length > 0) {
      const proceed = confirm(
        `Còn ${uncounted.length} mặt hàng chưa nhập số đếm. Bạn có muốn gán bằng tồn sổ sách để chốt không?`
      );
      if (!proceed) return;
    }

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    try {
      // Chuẩn bị SkuAdjustmentCosts map nếu có surplus
      const skuAdjustmentCosts: Record<string, number> = {};
      Object.entries(costMap).forEach(([skuId, costStr]) => {
        const val = Number(costStr);
        if (val > 0) {
          skuAdjustmentCosts[skuId] = val;
        }
      });

      const confirmed = await stockCountService.confirmStockCount(targetId, {
        note: detail.note,
        skuAdjustmentCosts: Object.keys(skuAdjustmentCosts).length > 0 ? skuAdjustmentCosts : undefined,
      });

      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      setDetail(confirmed);
      onRefresh();
      alert('Đã chốt kiểm kê và tạo chứng từ cân bằng tồn kho thành công!');
    } catch (err: any) {
      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      if (err.errorCode === 'STOCK_COUNT_DRIFT_DETECTED' || err.status === 409) {
        setSequenceDrift(true);
      }
      setErrorDetails(err);
    } finally {
      if (actionSeq === activeReqRef.current && countId === targetId) {
        setActionLoading(false);
      }
    }
  };

  // Hủy đợt kiểm kê
  const handleCancelCount = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;
    const reason = prompt('Nhập lý do hủy đợt kiểm kê:');
    if (reason === null) return;

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    setErrorDetails(null);
    try {
      const cancelled = await stockCountService.cancelStockCount(targetId, {
        reason: reason || undefined,
      });
      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      setDetail(cancelled);
      onRefresh();
      alert('Đã hủy đợt kiểm kê.');
    } catch (err: any) {
      if (actionSeq !== activeReqRef.current || countId !== targetId) return;
      setErrorDetails(err);
    } finally {
      if (actionSeq === activeReqRef.current && countId === targetId) {
        setActionLoading(false);
      }
    }
  };

  const statusCfg = detail
    ? STOCK_COUNT_STATUS_COLOR[detail.status] || {
        bg: 'bg-gray-100',
        text: 'text-gray-700',
        border: 'border-gray-200',
      }
    : { bg: 'bg-gray-100', text: 'text-gray-700', border: 'border-gray-200' };

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/40 flex justify-end">
      <div className="bg-white w-full max-w-4xl h-full shadow-2xl flex flex-col animate-in slide-in-from-right duration-200">
        {/* Drawer Header */}
        <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
          <div className="flex items-center gap-3">
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-xl font-bold text-gray-900">
                  {detail?.number || 'Đợt Kiểm Kê'}
                </h2>
                {detail && (
                  <span
                    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${statusCfg.bg} ${statusCfg.text} ${statusCfg.border}`}
                  >
                    {STOCK_COUNT_STATUS_LABEL[detail.status] || detail.status}
                  </span>
                )}
              </div>
              <p className="text-xs text-gray-500 mt-1">
                Kho: <span className="font-semibold text-gray-800">{detail?.warehouseName || '—'}</span> ·
                Chi nhánh: <span className="font-semibold text-gray-800">{detail?.branchName || '—'}</span>
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 p-2 rounded-lg hover:bg-gray-200 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body */}
        <div className="p-6 overflow-y-auto flex-1 space-y-4">
          {loading ? (
            <div className="py-24 text-center text-gray-500">
              <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-indigo-500 border-t-transparent mb-2"></div>
              <p className="text-sm">Đang tải chi tiết kiểm đếm...</p>
            </div>
          ) : detail ? (
            <>
              {detail.status === 'Confirmed' && (
                <div className="p-3 bg-blue-50 border border-blue-200 rounded-lg text-xs text-blue-900" role="status">
                  {detail.items.every((item) => item.countedQuantity === item.expectedQuantity)
                    ? 'Đợt kiểm kê đã xác nhận và không có chênh lệch. Tồn kho giữ nguyên nên không phát sinh bút toán mới; phiếu vẫn được lưu trong lịch sử kiểm kê.'
                    : `Đợt kiểm kê đã xác nhận. Xem lịch sử tại Kho & sổ kho → Sổ kho → Kiểm kê cân kho, tìm mã ${detail.number}.`}
                </div>
              )}
              {/* Sequence Drift Alert Banner (HTTP 409) */}
              {sequenceDrift && (
                <div className="p-4 bg-amber-50 border-2 border-amber-400 rounded-xl flex items-start gap-3 text-amber-900">
                  <AlertTriangle className="w-6 h-6 text-amber-600 flex-shrink-0 mt-0.5" />
                  <div>
                    <h4 className="font-bold text-sm">Phát hiện biến động kho xen ngang (Sequence Drift)</h4>
                    <p className="text-xs text-amber-800 mt-1">
                      Trong quá trình kiểm đếm, kho này đã phát sinh giao dịch nhập/xuất hoặc chuyển hàng khác, làm sai lệch sequence đối soát.
                      Hệ thống đã khóa chốt để đảm bảo an toàn. Bạn cần tải lại số liệu tồn kho snapshot mới để đối soát lại trước khi chốt.
                    </p>
                  </div>
                </div>
              )}

              {/* General Problem Details Error */}
              {errorDetails && !sequenceDrift && (
                <div className="p-4 bg-rose-50 border border-rose-300 rounded-xl text-rose-800 text-xs">
                  <div className="font-bold text-sm mb-1">{errorDetails.title}</div>
                  <p>{errorDetails.detail}</p>
                  {errorDetails.errorCode && (
                    <div className="mt-1 font-mono text-[10px] text-rose-600">
                      Mã lỗi: {errorDetails.errorCode}
                    </div>
                  )}
                </div>
              )}

              {/* Thông tin metadata */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 bg-gray-50 p-3.5 rounded-lg border border-gray-200 text-xs">
                <div>
                  <span className="text-gray-500 block">Ngày tạo:</span>
                  <span className="font-medium text-gray-800">
                    {detail.createdAt ? new Date(detail.createdAt).toLocaleString('vi-VN') : '—'}
                  </span>
                </div>
                <div>
                  <span className="text-gray-500 block">Ngày chốt:</span>
                  <span className="font-medium text-gray-800">
                    {detail.confirmedAt ? new Date(detail.confirmedAt).toLocaleString('vi-VN') : 'Chưa chốt'}
                  </span>
                </div>
                <div>
                  <span className="text-gray-500 block">Số dòng SKU:</span>
                  <span className="font-medium text-gray-800">{detail.items.length} mặt hàng</span>
                </div>
                <div>
                  <span className="text-gray-500 block">Mã chứng từ cân bằng:</span>
                  <span className="font-mono font-medium text-indigo-700">
                    {detail.generatedAdjustmentId ? detail.generatedAdjustmentId.slice(0, 8) : 'Chưa tạo'}
                  </span>
                </div>
              </div>

              {detail.note && (
                <div className="p-3 bg-blue-50 border border-blue-200 rounded-lg text-xs text-blue-900 flex items-start gap-2">
                  <Info className="w-4 h-4 text-blue-600 flex-shrink-0 mt-0.5" />
                  <span>
                    <strong>Ghi chú:</strong> {detail.note}
                  </span>
                </div>
              )}

              {/* Bảng chi tiết SKU kiểm đếm */}
              <div className="border border-gray-200 rounded-lg overflow-hidden">
                <div className="bg-gray-100 px-4 py-2.5 font-semibold text-xs text-gray-700 uppercase flex justify-between items-center">
                  <span>Danh sách mặt hàng kiểm kê</span>
                  <span className="text-gray-500 font-normal">
                    {isEditable ? '✏️ Nhập số lượng thực tế kiểm đếm' : '🔒 Đã khóa sổ'}
                  </span>
                </div>
                <div className="overflow-x-auto max-h-96">
                  <table className="w-full text-left text-xs border-collapse">
                    <thead>
                      <tr className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold">
                        <th className="py-2.5 px-3">Mã SKU / Tên SP</th>
                        <th className="py-2.5 px-3 text-right">Tồn sổ sách</th>
                        <th className="py-2.5 px-3 text-right">Thực tế đếm</th>
                        <th className="py-2.5 px-3 text-right">Chênh lệch</th>
                        <th className="py-2.5 px-3 text-right">Đơn giá Đ/C (nếu thừa)</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-200">
                      {detail.items.map((item) => {
                        const countedStr = countedMap[item.skuId] ?? (item.countedQuantity !== null && item.countedQuantity !== undefined ? String(item.countedQuantity) : '');
                        const countedNum = countedStr !== '' ? Number(countedStr) : null;
                        const diff = countedNum !== null ? countedNum - item.expectedQuantity : null;
                        const isSurplus = diff !== null && diff > 0;
                        const isShortage = diff !== null && diff < 0;

                        return (
                          <tr key={item.id || item.skuId} className="hover:bg-gray-50">
                            <td className="py-2.5 px-3">
                              <div className="font-semibold text-indigo-700">{item.skuCode}</div>
                              <div className="text-gray-500 text-[11px] truncate max-w-xs">
                                {item.productName || 'Sản phẩm'}
                              </div>
                            </td>
                            <td className="py-2.5 px-3 text-right font-medium text-gray-800">
                              {item.expectedQuantity}
                            </td>
                            <td className="py-2.5 px-3 text-right">
                              {isEditable ? (
                                <input
                                  type="number"
                                  min="0"
                                  step="any"
                                  value={countedStr}
                                  onChange={(e) =>
                                    setCountedMap({
                                      ...countedMap,
                                      [item.skuId]: e.target.value,
                                    })
                                  }
                                  placeholder={String(item.expectedQuantity)}
                                  className="w-24 text-right border border-gray-300 rounded px-2 py-1 text-xs focus:ring-1 focus:ring-indigo-500 outline-none"
                                />
                              ) : (
                                <span className="font-semibold text-gray-900">
                                  {item.countedQuantity ?? '—'}
                                </span>
                              )}
                            </td>
                            <td className="py-2.5 px-3 text-right">
                              {diff === null ? (
                                <span className="text-gray-400">Chưa đếm</span>
                              ) : isSurplus ? (
                                <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-bold bg-emerald-100 text-emerald-800">
                                  +{diff} (Thừa)
                                </span>
                              ) : isShortage ? (
                                <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-bold bg-rose-100 text-rose-800">
                                  {diff} (Thiếu)
                                </span>
                              ) : (
                                <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-medium bg-gray-100 text-gray-700">
                                  0 (Khớp)
                                </span>
                              )}
                            </td>
                            <td className="py-2.5 px-3 text-right">
                              {isSurplus && isEditable ? (
                                <input
                                  type="number"
                                  min="0"
                                  step="any"
                                  value={costMap[item.skuId] || ''}
                                  onChange={(e) =>
                                    setCostMap({
                                      ...costMap,
                                      [item.skuId]: e.target.value,
                                    })
                                  }
                                  placeholder="Đơn giá WAC"
                                  className="w-28 text-right border border-amber-300 rounded px-2 py-1 text-xs focus:ring-1 focus:ring-amber-500 outline-none bg-amber-50/40"
                                />
                              ) : (
                                <span className="text-gray-500">
                                  {item.adjustmentUnitCost
                                    ? `${item.adjustmentUnitCost.toLocaleString('vi-VN')} đ`
                                    : '—'}
                                </span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            </>
          ) : null}
        </div>

        {/* Drawer Footer Actions */}
        {detail && isEditable && (
          <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex flex-wrap justify-between items-center gap-3">
            <button
              onClick={handleCancelCount}
              disabled={actionLoading}
              className="px-3 py-1.5 text-xs font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg flex items-center gap-1.5"
            >
              <Ban className="w-4 h-4" />
              Hủy đợt kiểm kê
            </button>

            <div className="flex gap-2">
              <button
                onClick={handleSaveCounts}
                disabled={actionLoading}
                className="px-4 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 flex items-center gap-1.5 shadow-sm"
              >
                <Save className="w-4 h-4 text-gray-600" />
                Lưu kết quả đếm
              </button>

              <button
                onClick={handleConfirmCount}
                disabled={actionLoading}
                className="px-5 py-2 text-xs font-semibold text-white bg-emerald-600 hover:bg-emerald-700 rounded-lg flex items-center gap-1.5 shadow-sm"
              >
                <CheckCircle2 className="w-4 h-4" />
                {actionLoading ? 'Đang hạch toán...' : 'Chốt kiểm kê & Cân bằng tồn'}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
