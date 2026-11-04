import React, { useState, useEffect, useRef } from 'react';
import type { StockCount } from '../../types/stockCount';
import {
  STOCK_COUNT_STATUS_LABEL,
  STOCK_COUNT_STATUS_COLOR,
} from '../../types/stockCount';
import { stockCountService, type ProblemDetailsError } from '../../services/stockCountService';
import { useAuth } from '../../context/AuthContext';
import {
  X,
  AlertTriangle,
  CheckCircle2,
  Save,
  Ban,
  Send,
  RotateCcw,
  ShieldAlert,
  Clock,
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
  const { user } = useAuth();
  const isOwner = user?.role === 'Owner';

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
  const busyIdsRef = useRef<Set<string>>(new Set());
  const currentCountIdRef = useRef<string | null>(countId);
  currentCountIdRef.current = countId;

  useEffect(() => {
    if (!countId) {
      setDetail(null);
      setActionLoading(false);
      return;
    }

    const reqSeq = ++activeReqRef.current;
    setDetail(null);
    setLoading(true);
    setActionLoading(busyIdsRef.current.has(countId));
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

  const isCounting = detail?.status === 'Draft' || detail?.status === 'Counting';
  const isPending = detail?.status === 'PendingApproval';
  const isConfirmed = detail?.status === 'Confirmed';

  // Lưu bản đếm thực tế (POST /api/stock-counts/{id}/record)
  const handleSaveCounts = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    try {
      const itemsToRecord = detail.items.map((item) => {
        const valStr = countedMap[item.skuId];
        const countedVal = valStr !== undefined && valStr.trim() !== '' ? Number(valStr) : 0;
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

      if (currentCountIdRef.current !== targetId) return;
      setDetail(updated);
      onRefresh();
      alert('Đã lưu kết quả kiểm đếm lên máy chủ thành công!');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      if (err.errorCode === 'STOCK_COUNT_DRIFT_DETECTED' || err.status === 409) {
        setSequenceDrift(true);
      }
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
      }
    }
  };

  // Nộp kết quả kiểm đếm để trình duyệt (Counting -> PendingApproval) (U03)
  const handleSubmitForApproval = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    // Kiểm tra các mặt hàng chưa đếm
    const uncounted = detail.items.filter(
      (item) => countedMap[item.skuId] === undefined || countedMap[item.skuId].trim() === ''
    );
    if (uncounted.length > 0) {
      const proceed = confirm(
        `Còn ${uncounted.length} mặt hàng chưa nhập số đếm. Bạn có muốn gán số đếm bằng 0 cho các mặt hàng này và nộp duyệt không?`
      );
      if (!proceed) return;
    }

    const note = prompt('Ghi chú nộp kiểm kê để trình duyệt (tùy chọn):', detail.note || '');
    if (note === null) return; // Người dùng hủy

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    try {
      // Tự động record trước khi nộp
      const itemsToRecord = detail.items.map((item) => {
        const valStr = countedMap[item.skuId];
        const countedVal = valStr !== undefined && valStr.trim() !== '' ? Number(valStr) : 0;
        const costStr = costMap[item.skuId];
        const costVal = costStr && Number(costStr) > 0 ? Number(costStr) : undefined;
        return {
          skuId: item.skuId,
          countedQuantity: countedVal,
          adjustmentUnitCost: costVal,
        };
      });

      await stockCountService.recordCounts(targetId, { items: itemsToRecord });

      const submitted = await stockCountService.submitStockCount(targetId, {
        note: note.trim() || undefined,
      });

      if (currentCountIdRef.current !== targetId) return;
      setDetail(submitted);
      onRefresh();
      alert('Đã nộp kết quả kiểm đếm thành công! Đợt kiểm kê chuyển sang trạng thái "Chờ duyệt chênh lệch" (PendingApproval).');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
      }
    }
  };

  // Phê duyệt cân bằng tồn (Chỉ Owner) (PendingApproval -> Confirmed)
  const handleConfirmCount = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    if (!isOwner) {
      setErrorDetails({
        title: 'Quyền truy cập bị từ chối (403 Forbidden)',
        detail: 'Chỉ Chủ cửa hàng (Owner) mới có quyền phê duyệt cân bằng tồn kho theo quy định kiểm kê U03.',
        errorCode: 'FORBIDDEN',
        status: 403,
      });
      return;
    }

    // Nhập ghi chú duyệt nếu tự duyệt (self-counter)
    let note = detail.note;
    const promptNote = prompt(
      'Nhập ghi chú phê duyệt cân bằng tồn kho (Bắt buộc nếu bạn là người trực tiếp kiểm đếm):',
      detail.note || ''
    );
    if (promptNote === null) return;
    note = promptNote.trim();

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);
    setSequenceDrift(false);

    try {
      const skuAdjustmentCosts: Record<string, number> = {};
      Object.entries(costMap).forEach(([skuId, costStr]) => {
        const val = Number(costStr);
        if (val > 0) {
          skuAdjustmentCosts[skuId] = val;
        }
      });

      const confirmed = await stockCountService.confirmStockCount(targetId, {
        note: note || undefined,
        skuAdjustmentCosts: Object.keys(skuAdjustmentCosts).length > 0 ? skuAdjustmentCosts : undefined,
      });

      if (currentCountIdRef.current !== targetId) return;
      setDetail(confirmed);
      onRefresh();
      alert('Đã phê duyệt và chốt kiểm kê thành công! Số dư tồn kho đã được cân bằng chính thức.');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      if (err.errorCode === 'STOCK_COUNT_DRIFT_DETECTED' || err.status === 409) {
        setSequenceDrift(true);
      }
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
      }
    }
  };

  // Owner yêu cầu đếm lại (Request Recount)
  const handleRequestRecount = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    if (!isOwner) {
      alert('Chỉ Owner mới có quyền yêu cầu kiểm đếm lại.');
      return;
    }

    const reason = prompt('Nhập lý do yêu cầu kiểm đếm lại (bắt buộc):');
    if (!reason || !reason.trim()) {
      alert('Lý do yêu cầu kiểm đếm lại là bắt buộc.');
      return;
    }

    // Mặc định chọn tất cả SKU có chênh lệch hoặc toàn bộ
    const diffSkus = detail.items.filter((i) => i.differenceQuantity !== 0).map((i) => i.skuId);
    const skuIdsToRecount = diffSkus.length > 0 ? diffSkus : detail.items.map((i) => i.skuId);

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);

    try {
      const updated = await stockCountService.requestRecount(targetId, {
        skuIds: skuIdsToRecount,
        reason: reason.trim(),
      });

      if (currentCountIdRef.current !== targetId) return;
      setDetail(updated);
      onRefresh();
      alert('Đã gửi yêu cầu kiểm đếm lại thành công!');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
      }
    }
  };

  // Đếm lại các SKU bị lệch sequence / yêu cầu đếm lại (Recount)
  const handleRecountDrifted = async () => {
    if (!detail || detail.id !== countId || loading || actionLoading) return;

    const diffSkus = detail.items.map((i) => i.skuId);
    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);

    try {
      const updated = await stockCountService.recountDrifted(targetId, {
        skuIds: diffSkus,
      });

      if (currentCountIdRef.current !== targetId) return;
      setDetail(updated);
      setSequenceDrift(false);
      onRefresh();
      alert('Đã cập nhật yêu cầu đếm lại! Phiếu chuyển về trạng thái tiếp tục kiểm đếm.');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
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
    busyIdsRef.current.add(targetId);
    setActionLoading(true);
    setErrorDetails(null);

    try {
      const cancelled = await stockCountService.cancelStockCount(targetId, {
        reason: reason.trim() || undefined,
      });

      if (currentCountIdRef.current !== targetId) return;
      setDetail(cancelled);
      onRefresh();
      alert('Đã hủy đợt kiểm kê thành công.');
    } catch (err: any) {
      if (currentCountIdRef.current !== targetId) return;
      setErrorDetails(err);
    } finally {
      busyIdsRef.current.delete(targetId);
      if (currentCountIdRef.current === targetId || actionSeq === activeReqRef.current) {
        setActionLoading(busyIdsRef.current.has(currentCountIdRef.current || ''));
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
              {/* Status Banners */}
              {isConfirmed && (
                <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-lg text-xs text-emerald-900 flex items-center gap-2" role="status">
                  <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
                  <span>
                    {detail.items.every((item) => item.countedQuantity === item.expectedQuantity)
                      ? 'Đợt kiểm kê đã được phê duyệt và không có chênh lệch. Tồn kho giữ nguyên; phiếu được lưu trong lịch sử kiểm kê.'
                      : `Đợt kiểm kê đã được phê duyệt chốt cân bằng tồn kho. Chứng từ điều chỉnh đã được ghi sổ tự động.`}
                  </span>
                </div>
              )}

              {isPending && (
                <div className="p-4 bg-blue-50 border border-blue-200 rounded-xl text-blue-900 text-xs flex items-start gap-3">
                  <Clock className="w-5 h-5 text-blue-600 shrink-0 mt-0.5" />
                  <div>
                    <h4 className="font-bold text-sm">Đợt kiểm kê đang chờ phê duyệt chênh lệch (PendingApproval)</h4>
                    <p className="mt-1 text-blue-800">
                      Số lượng kiểm đếm thực tế đã được nộp và khóa đối soát. Tồn kho sổ sách CHƯA bị thay đổi cho đến khi Chủ cửa hàng (Owner) chính thức phê duyệt chốt cân bằng tồn.
                    </p>
                    {!isOwner && (
                      <p className="mt-1 font-semibold text-amber-800">
                        * Tài khoản của bạn là Nhân viên (Staff). Chỉ có Chủ cửa hàng (Owner) mới có quyền duyệt chốt cân bằng tồn kho.
                      </p>
                    )}
                  </div>
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
                      Hệ thống đã khóa chốt để đảm bảo an toàn. Bạn cần bấm &quot;Đếm lại SKU lệch&quot; để đối soát lại trước khi nộp/chốt.
                    </p>
                    <button
                      type="button"
                      onClick={handleRecountDrifted}
                      className="mt-2 inline-flex items-center gap-1.5 px-3 py-1.5 bg-amber-600 hover:bg-amber-700 text-white font-semibold rounded-lg text-xs"
                    >
                      <RotateCcw className="w-3.5 h-3.5" />
                      <span>Đếm lại các SKU bị lệch sequence</span>
                    </button>
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

              {/* Metadata */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 bg-gray-50 p-3.5 rounded-lg border border-gray-200 text-xs">
                <div>
                  <span className="text-gray-500 block">Ngày tạo:</span>
                  <span className="font-medium text-gray-800">
                    {detail.createdAt ? new Date(detail.createdAt).toLocaleString('vi-VN') : '—'}
                  </span>
                </div>
                <div>
                  <span className="text-gray-500 block">Thời gian nộp:</span>
                  <span className="font-medium text-gray-800">
                    {detail.submittedAt ? new Date(detail.submittedAt).toLocaleString('vi-VN') : 'Chưa nộp'}
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
                  <span className="font-bold text-gray-800">{detail.items.length} mặt hàng</span>
                </div>
              </div>

              {/* Ghi chú & Lý do đột xuất */}
              {(detail.adHocReason || detail.note) && (
                <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg text-xs space-y-1">
                  {detail.adHocReason && (
                    <div>
                      <span className="font-bold text-slate-700">Lý do kiểm kê đột xuất: </span>
                      <span className="text-slate-900">{detail.adHocReason}</span>
                    </div>
                  )}
                  {detail.note && (
                    <div>
                      <span className="font-bold text-slate-700">Ghi chú: </span>
                      <span className="text-slate-900">{detail.note}</span>
                    </div>
                  )}
                </div>
              )}

              {/* Table of items */}
              <div className="border border-gray-200 rounded-lg overflow-hidden">
                <div className="bg-gray-50 px-4 py-2.5 border-b border-gray-200 font-semibold text-xs text-gray-700 flex justify-between">
                  <span>Danh sách mặt hàng kiểm kê (theo đơn vị gốc)</span>
                  <span className="text-gray-500 font-normal">
                    {isCounting ? 'Đang mở cho phép nhập số đếm' : isPending ? 'Đã khóa số đếm - Chờ duyệt' : 'Đã hoàn tất'}
                  </span>
                </div>

                <div className="overflow-x-auto max-h-[350px]">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-gray-100/70 border-b border-gray-200 text-gray-600 uppercase text-[10px] tracking-wider sticky top-0 z-10">
                      <tr>
                        <th className="py-2 px-3">Mặt hàng / SKU</th>
                        <th className="py-2 px-3 text-right">Tồn sổ sách (gốc)</th>
                        <th className="py-2 px-3 text-right">Thực tế (gốc)</th>
                        <th className="py-2 px-3 text-right">Chênh lệch</th>
                        <th className="py-2 px-3 text-right">Đơn giá WAC (nếu thừa)</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100 font-mono">
                      {detail.items.map((item) => {
                        const valStr = countedMap[item.skuId];
                        const countedNum = valStr !== undefined && valStr.trim() !== '' ? Number(valStr) : null;
                        const diff = countedNum !== null ? countedNum - item.expectedQuantity : null;
                        const isSurplus = diff !== null && diff > 0;
                        const isShortage = diff !== null && diff < 0;

                        return (
                          <tr key={item.id} className="hover:bg-gray-50/80">
                            <td className="py-2.5 px-3 font-sans">
                              <div className="font-semibold text-gray-900">{item.productName}</div>
                              <div className="text-[11px] text-gray-500 font-mono">{item.skuCode}</div>
                            </td>
                            <td className="py-2.5 px-3 text-right text-gray-700 font-bold">
                              {item.expectedQuantity}
                            </td>
                            <td className="py-2.5 px-3 text-right">
                              {isCounting ? (
                                <input
                                  type="number"
                                  min="0"
                                  step="any"
                                  value={countedMap[item.skuId] || ''}
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
                              {isSurplus && isCounting ? (
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
        {detail && (
          <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex flex-wrap justify-between items-center gap-3">
            {/* Cancel action */}
            {isCounting && (
              <button
                type="button"
                onClick={handleCancelCount}
                disabled={actionLoading}
                className="px-3 py-1.5 text-xs font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg flex items-center gap-1.5"
              >
                <Ban className="w-4 h-4" />
                Hủy đợt kiểm kê
              </button>
            )}

            {/* In Counting state: Record & Submit */}
            {isCounting && (
              <div className="flex items-center gap-2 ml-auto">
                <button
                  type="button"
                  onClick={handleSaveCounts}
                  disabled={actionLoading}
                  className="px-4 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 flex items-center gap-1.5 shadow-sm"
                >
                  <Save className="w-4 h-4 text-gray-600" />
                  Lưu số đếm
                </button>

                <button
                  type="button"
                  onClick={handleSubmitForApproval}
                  disabled={actionLoading}
                  className="px-5 py-2 text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg flex items-center gap-1.5 shadow-sm"
                >
                  <Send className="w-4 h-4" />
                  {actionLoading ? 'Đang nộp...' : 'Nộp kết quả để duyệt (Submit)'}
                </button>
              </div>
            )}

            {/* In PendingApproval state: Confirm & Request Recount */}
            {isPending && (
              <div className="flex items-center gap-2 ml-auto">
                {isOwner && (
                  <button
                    type="button"
                    onClick={handleRequestRecount}
                    disabled={actionLoading}
                    className="px-4 py-2 text-xs font-semibold text-amber-800 bg-amber-50 hover:bg-amber-100 border border-amber-300 rounded-lg flex items-center gap-1.5 shadow-sm"
                  >
                    <RotateCcw className="w-4 h-4 text-amber-700" />
                    Yêu cầu đếm lại
                  </button>
                )}

                <button
                  type="button"
                  onClick={handleConfirmCount}
                  disabled={actionLoading || !isOwner}
                  title={!isOwner ? 'Chỉ Chủ cửa hàng (Owner) mới có quyền duyệt chốt tồn kho' : undefined}
                  className={`px-5 py-2 text-xs font-semibold rounded-lg flex items-center gap-1.5 shadow-sm ${
                    isOwner
                      ? 'text-white bg-emerald-600 hover:bg-emerald-700'
                      : 'text-gray-400 bg-gray-200 cursor-not-allowed'
                  }`}
                >
                  {isOwner ? (
                    <CheckCircle2 className="w-4 h-4" />
                  ) : (
                    <ShieldAlert className="w-4 h-4 text-gray-400" />
                  )}
                  {actionLoading
                    ? 'Đang hạch toán...'
                    : isOwner
                    ? 'Phê duyệt cân bằng tồn (Confirm)'
                    : 'Chờ Owner phê duyệt'}
                </button>
              </div>
            )}

            {isConfirmed && (
              <div className="text-xs text-slate-500 font-medium ml-auto">
                Đợt kiểm kê đã hoàn tất.
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
