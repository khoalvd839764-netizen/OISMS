import React, { useEffect, useRef, useState } from 'react';
import {
  AlertCircle,
  Ban,
  Check,
  ChevronDown,
  ChevronRight,
  RefreshCw,
} from 'lucide-react';
import { Badge, Button, Input, Modal, Select } from '../ui';
import { pricingService } from '../../services/pricingService';
import { useAuth } from '../../context/AuthContext';
import type { Sku } from '../../types/domain';
import type { PriceChangeRequestResponse, PriceHistoryItem } from '../../types/pricing';

interface PriceManagementModalProps {
  isOpen: boolean;
  onClose: () => void;
  initialSku?: Sku | null;
  allSkus?: Sku[];
  onDataChanged?: () => void;
}

export const PriceManagementModal: React.FC<PriceManagementModalProps> = ({
  isOpen,
  onClose,
  initialSku,
  allSkus = [],
  onDataChanged,
}) => {
  const { account } = useAuth();
  const isOwner = account?.role === 'Owner';

  const [tab, setTab] = useState<'requests' | 'history'>('requests');

  // Tab 1: Phiếu đổi giá
  const [requests, setRequests] = useState<PriceChangeRequestResponse[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [loadingRequests, setLoadingRequests] = useState(false);
  const [requestsError, setRequestsError] = useState<string | null>(null);
  const [expandedRequestId, setExpandedRequestId] = useState<string | null>(null);

  // Thao tác Duyệt / Từ chối phiếu
  const [processingId, setProcessingId] = useState<string | null>(null);
  const [rejectModalId, setRejectModalId] = useState<string | null>(null);
  const [rejectNote, setRejectNote] = useState<string>('');
  const [actionError, setActionError] = useState<string | null>(null);

  // Tab 2: Lịch sử giá & Lịch giá tương lai theo SKU
  const [selectedSkuId, setSelectedSkuId] = useState<string>(initialSku?.id || allSkus[0]?.id || '');
  const [historyItems, setHistoryItems] = useState<PriceHistoryItem[]>([]);
  const [loadingHistory, setLoadingHistory] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

  // Hủy giá đã lên lịch
  const [cancelModalPriceId, setCancelModalPriceId] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState<string>('');
  const [requestsTotal, setRequestsTotal] = useState(0);
  const requestsSeqRef = useRef(0);
  const historySeqRef = useRef(0);

  useEffect(() => {
    if (!isOpen) return;
    if (initialSku) {
      setSelectedSkuId(initialSku.id);
    } else if (allSkus.length > 0 && !selectedSkuId) {
      setSelectedSkuId(allSkus[0].id);
    }
    void loadRequests();
  }, [isOpen, statusFilter]);

  useEffect(() => {
    if (!isOpen || tab !== 'history' || !selectedSkuId) return;
    void loadHistory(selectedSkuId);
  }, [isOpen, tab, selectedSkuId]);

  const loadRequests = async () => {
    const seq = ++requestsSeqRef.current;
    setLoadingRequests(true);
    setRequestsError(null);
    try {
      const res = await pricingService.getRequests(statusFilter, 1, 50);
      if (seq !== requestsSeqRef.current) return;
      setRequests(res.items);
      setRequestsTotal(res.totalCount);
    } catch (err: any) {
      if (seq !== requestsSeqRef.current) return;
      setRequests([]);
      setRequestsError(err?.response?.data?.detail || 'Không thể tải danh sách phiếu đổi giá.');
    } finally {
      if (seq === requestsSeqRef.current) setLoadingRequests(false);
    }
  };

  const loadHistory = async (skuId: string) => {
    const seq = ++historySeqRef.current;
    setLoadingHistory(true);
    setHistoryError(null);
    setHistoryItems([]);
    try {
      const items = await pricingService.getPriceHistory(skuId);
      if (seq !== historySeqRef.current) return;
      setHistoryItems(items);
    } catch (err: any) {
      if (seq !== historySeqRef.current) return;
      setHistoryError(err?.response?.data?.detail || 'Không thể tải lịch sử giá của SKU.');
    } finally {
      if (seq === historySeqRef.current) setLoadingHistory(false);
    }
  };

  const handleApprove = async (id: string) => {
    if (processingId) return;
    setActionError(null);
    setProcessingId(id);
    try {
      await pricingService.approveRequest(id);
      await loadRequests();
      onDataChanged?.();
    } catch (err: any) {
      setActionError(err?.response?.data?.detail || err?.response?.data?.message || 'Không thể phê duyệt phiếu đổi giá.');
    } finally {
      setProcessingId(null);
    }
  };

  const handleReject = async () => {
    if (!rejectModalId || processingId) return;
    setActionError(null);
    setProcessingId(rejectModalId);
    try {
      await pricingService.rejectRequest(rejectModalId, rejectNote);
      setRejectModalId(null);
      setRejectNote('');
      await loadRequests();
      onDataChanged?.();
    } catch (err: any) {
      setActionError(err?.response?.data?.detail || err?.response?.data?.message || 'Không thể từ chối phiếu đổi giá.');
    } finally {
      setProcessingId(null);
    }
  };

  const handleCancelScheduled = async () => {
    if (!cancelModalPriceId || processingId) return;
    setActionError(null);
    setProcessingId(cancelModalPriceId);
    try {
      await pricingService.cancelScheduledPrice(cancelModalPriceId, cancelReason);
      setCancelModalPriceId(null);
      setCancelReason('');
      if (selectedSkuId) {
        await loadHistory(selectedSkuId);
      }
      onDataChanged?.();
    } catch (err: any) {
      setActionError(err?.response?.data?.detail || err?.response?.data?.message || 'Không thể hủy giá đã lên lịch.');
    } finally {
      setProcessingId(null);
    }
  };

  if (!isOpen) return null;

  return (
    <Modal title="Quản lý Bảng giá & Lịch sử giá" onClose={onClose}>
      <div style={{ padding: '0 4px' }}>
        <div className="tabs" style={{ marginBottom: '14px' }}>
          <button
            className={tab === 'requests' ? 'active' : ''}
            onClick={() => setTab('requests')}
          >
            Phiếu đổi giá ({requests.length})
          </button>
          <button
            className={tab === 'history' ? 'active' : ''}
            onClick={() => setTab('history')}
          >
            Lịch sử giá & Lịch tương lai
          </button>
        </div>

        {actionError && (
          <div
            style={{
              background: '#fef2f2',
              border: '1px solid #ef4444',
              color: '#b91c1c',
              padding: '10px 14px',
              borderRadius: '8px',
              fontSize: '12px',
              marginBottom: '12px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
            }}
          >
            <AlertCircle size={16} />
            <span>{actionError}</span>
          </div>
        )}

        {tab === 'requests' ? (
          <div>
            <div className="toolbar" style={{ padding: '8px 0', borderBottom: '1px solid #e2e8f0', marginBottom: '12px' }}>
              <span style={{ fontSize: '11px', fontWeight: 600, color: '#475569' }}>Lọc trạng thái:</span>
              <Select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                style={{ minWidth: '160px' }}
              >
                <option value="all">Tất cả trạng thái</option>
                <option value="Pending">Chờ duyệt (Pending)</option>
                <option value="Approved">Đã duyệt (Approved)</option>
                <option value="Rejected">Đã từ chối (Rejected)</option>
              </Select>
              <Button
                variant="secondary"
                onClick={loadRequests}
                disabled={loadingRequests}
                style={{ marginLeft: 'auto' }}
              >
                <RefreshCw size={14} className={loadingRequests ? 'animate-spin' : ''} />
                Làm mới
              </Button>
            </div>

            {requestsTotal > requests.length && (
              <p style={{ fontSize: '11px', color: '#b45309', margin: '0 0 8px' }}>
                Đang hiển thị {requests.length}/{requestsTotal} phiếu mới nhất. Lọc theo trạng thái để thu hẹp danh sách.
              </p>
            )}

            {requestsError && (
              <div style={{ color: '#b91c1c', padding: '12px', background: '#fee2e2', borderRadius: '8px', fontSize: '12px' }}>
                {requestsError}
              </div>
            )}

            {loadingRequests ? (
              <div style={{ textAlign: 'center', padding: '36px', color: '#64748b' }}>
                Đang tải danh sách phiếu đổi giá...
              </div>
            ) : requestsError ? null : requests.length === 0 ? (
              <div style={{ textAlign: 'center', padding: '36px', color: '#64748b' }}>
                Không có phiếu đổi giá nào trong danh sách.
              </div>
            ) : (
              <div className="table-wrap" style={{ maxHeight: '420px', overflowY: 'auto' }}>
                <table>
                  <thead>
                    <tr>
                      <th style={{ width: '32px' }}></th>
                      <th>Lý do & Mã phiếu</th>
                      <th>Thời gian tạo</th>
                      <th>Hiệu lực</th>
                      <th>Trạng thái</th>
                      <th style={{ textAlign: 'right' }}>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {requests.map((r) => {
                      const isExpanded = expandedRequestId === r.id;
                      const statusTone =
                        r.status === 'Approved'
                          ? 'success'
                          : r.status === 'Pending'
                          ? 'warning'
                          : 'danger';

                      return (
                        <React.Fragment key={r.id}>
                          <tr>
                            <td>
                              <button
                                type="button"
                                style={{ background: 'transparent', border: 0, cursor: 'pointer', padding: 4 }}
                                onClick={() => setExpandedRequestId(isExpanded ? null : r.id)}
                              >
                                {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                              </button>
                            </td>
                            <td>
                              <strong>{r.reason}</strong>
                              <span style={{ display: 'block', fontSize: '10px', color: '#64748b' }}>
                                ID: {r.id.slice(0, 8)}... · {r.items.length} mặt hàng
                              </span>
                            </td>
                            <td>
                              {new Date(r.createdAt).toLocaleString('vi-VN')}
                            </td>
                            <td>
                              {r.requestedValidFrom ? (
                                <span style={{ color: '#2563eb' }}>
                                  {new Date(r.requestedValidFrom).toLocaleString('vi-VN')}
                                </span>
                              ) : (
                                <span>Áp dụng ngay</span>
                              )}
                            </td>
                            <td>
                              <Badge tone={statusTone}>{r.status}</Badge>
                            </td>
                            <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                              {isOwner && r.status === 'Pending' ? (
                                <div style={{ display: 'inline-flex', gap: '6px' }}>
                                  <Button
                                    variant="primary"
                                    onClick={() => handleApprove(r.id)}
                                    disabled={processingId === r.id}
                                    style={{ minHeight: '30px', padding: '0 8px', fontSize: '11px' }}
                                  >
                                    <Check size={14} /> Duyệt
                                  </Button>
                                  <Button
                                    variant="danger"
                                    onClick={() => {
                                      setRejectModalId(r.id);
                                      setRejectNote('');
                                    }}
                                    disabled={processingId === r.id}
                                    style={{ minHeight: '30px', padding: '0 8px', fontSize: '11px' }}
                                  >
                                    <Ban size={14} /> Từ chối
                                  </Button>
                                </div>
                              ) : (
                                <span style={{ fontSize: '11px', color: '#94a3b8' }}>
                                  {r.status === 'Approved' ? 'Đã duyệt' : r.status === 'Rejected' ? 'Đã từ chối' : 'Chờ Owner duyệt'}
                                </span>
                              )}
                            </td>
                          </tr>

                          {/* Chi tiết dòng phiếu khi bấm mở rộng */}
                          {isExpanded && (
                            <tr>
                              <td colSpan={6} style={{ background: '#f8fafc', padding: '12px 16px' }}>
                                <div style={{ fontSize: '11px', marginBottom: '8px' }}>
                                  <strong>Chi tiết danh sách thay đổi giá ({r.items.length} dòng):</strong>
                                  {r.reviewNote && (
                                    <div style={{ color: '#b91c1c', marginTop: '4px' }}>
                                      Ghi chú duyệt/từ chối: {r.reviewNote}
                                    </div>
                                  )}
                                </div>
                                <table style={{ background: '#fff', border: '1px solid #e2e8f0', borderRadius: '6px' }}>
                                  <thead>
                                    <tr>
                                      <th>Mã SKU</th>
                                      <th>Đơn vị tính</th>
                                      <th>Bảng giá</th>
                                      <th>Giá mới</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {r.items.map((it) => (
                                      <tr key={it.id}>
                                        <td><strong>{it.skuCode}</strong></td>
                                        <td>{it.unitName}</td>
                                        <td>
                                          <Badge tone={it.priceListCode === 'retail' ? 'info' : 'warning'}>
                                            {it.priceListCode === 'retail' ? 'Giá bán lẻ' : 'Giá bán sỉ'}
                                          </Badge>
                                        </td>
                                        <td>
                                          <strong style={{ color: 'var(--brand)' }}>
                                            {it.newPrice.toLocaleString('vi-VN')} ₫
                                          </strong>
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </td>
                            </tr>
                          )}
                        </React.Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        ) : (
          <div>
            {/* Tab 2: Lịch sử giá */}
            <div className="toolbar" style={{ padding: '8px 0', borderBottom: '1px solid #e2e8f0', marginBottom: '12px' }}>
              <span style={{ fontSize: '11px', fontWeight: 600, color: '#475569' }}>Chọn SKU tra cứu:</span>
              <Select
                value={selectedSkuId}
                onChange={(e) => setSelectedSkuId(e.target.value)}
                style={{ minWidth: '240px' }}
              >
                {allSkus.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.code} — {s.name}
                  </option>
                ))}
              </Select>
              <Button
                variant="secondary"
                onClick={() => selectedSkuId && loadHistory(selectedSkuId)}
                disabled={loadingHistory}
                style={{ marginLeft: 'auto' }}
              >
                <RefreshCw size={14} className={loadingHistory ? 'animate-spin' : ''} />
                Làm mới
              </Button>
            </div>

            {historyError && (
              <div style={{ color: '#b91c1c', padding: '12px', background: '#fee2e2', borderRadius: '8px', fontSize: '12px' }}>
                {historyError}
              </div>
            )}

            {loadingHistory ? (
              <div style={{ textAlign: 'center', padding: '36px', color: '#64748b' }}>
                Đang tải lịch sử giá...
              </div>
            ) : historyError ? null : historyItems.length === 0 ? (
              <div style={{ textAlign: 'center', padding: '36px', color: '#64748b' }}>
                Chưa có lịch sử giá nào được ghi nhận cho SKU này.
              </div>
            ) : (
              <div className="table-wrap" style={{ maxHeight: '420px', overflowY: 'auto' }}>
                <table>
                  <thead>
                    <tr>
                      <th>Bảng giá</th>
                      <th>Đơn vị tính</th>
                      <th>Đơn giá</th>
                      <th>Thời gian hiệu lực</th>
                      <th>Trạng thái</th>
                      <th style={{ textAlign: 'right' }}>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {historyItems.map((h) => {
                      const now = Date.now();
                      const validFromTime = new Date(h.validFrom).getTime();
                      const isFuture = validFromTime > now && !h.cancelledAt;
                      const isCancelled = Boolean(h.cancelledAt);

                      let statusBadge = <Badge tone="neutral">Đã hết hạn</Badge>;
                      if (isCancelled) {
                        statusBadge = <Badge tone="danger">Đã hủy</Badge>;
                      } else if (isFuture) {
                        statusBadge = <Badge tone="info">Lên lịch tương lai</Badge>;
                      } else if (h.validTo && new Date(h.validTo).getTime() <= now) {
                        statusBadge = <Badge tone="neutral">Đã hết hạn</Badge>;
                      } else if (h.isActive) {
                        statusBadge = <Badge tone="success">Đang áp dụng</Badge>;
                      }

                      return (
                        <tr key={h.id}>
                          <td>
                            <Badge tone={h.priceListCode === 'retail' ? 'info' : 'warning'}>
                              {h.priceListCode === 'retail' ? 'Bán lẻ' : 'Bán sỉ'}
                            </Badge>
                          </td>
                          <td>
                            <span style={{ fontSize: '11px', color: '#475569' }}>
                              {allSkus.find((s) => s.id === selectedSkuId)?.units?.find((u) => u.unitId === h.unitId)?.unitName || 'Đơn vị không còn hiệu lực'}
                            </span>
                          </td>
                          <td>
                            <strong style={{ fontSize: '12px', color: 'var(--brand)' }}>
                              {h.price.toLocaleString('vi-VN')} ₫
                            </strong>
                          </td>
                          <td>
                            <div>
                              <span>Từ: {new Date(h.validFrom).toLocaleString('vi-VN')}</span>
                              {h.validTo && (
                                <span style={{ display: 'block', color: '#64748b', fontSize: '10px' }}>
                                  Đến: {new Date(h.validTo).toLocaleString('vi-VN')}
                                </span>
                              )}
                              {h.cancelledAt && (
                                <span style={{ display: 'block', color: '#b91c1c', fontSize: '10px' }}>
                                  Hủy ngày: {new Date(h.cancelledAt).toLocaleString('vi-VN')} ({h.cancelReason})
                                </span>
                              )}
                            </div>
                          </td>
                          <td>{statusBadge}</td>
                          <td style={{ textAlign: 'right' }}>
                            {isOwner && isFuture && (
                              <Button
                                variant="danger"
                                onClick={() => {
                                  setCancelModalPriceId(h.id);
                                  setCancelReason('');
                                }}
                                disabled={processingId === h.id}
                                style={{ minHeight: '30px', padding: '0 8px', fontSize: '11px' }}
                              >
                                <Ban size={14} /> Hủy lịch giá
                              </Button>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        <div className="modal-actions">
          <Button variant="secondary" onClick={onClose}>
            Đóng
          </Button>
        </div>
      </div>

      {/* Modal xác nhận Từ chối phiếu đổi giá */}
      {rejectModalId && (
        <Modal title="Từ chối phê duyệt phiếu đổi giá" onClose={() => setRejectModalId(null)}>
          <div style={{ padding: '8px 12px' }}>
            <p style={{ fontSize: '12px', color: '#475569', marginBottom: '10px' }}>
              Vui lòng nhập lý do từ chối phiếu đổi giá này để thông báo cho người lập phiếu:
            </p>
            <Input
              value={rejectNote}
              onChange={(e) => setRejectNote(e.target.value)}
              placeholder="Ví dụ: Giá chưa phù hợp chính sách, cần xem xét lại..."
              maxLength={500}
            />
            <div className="modal-actions" style={{ marginTop: '16px' }}>
              <Button variant="secondary" onClick={() => setRejectModalId(null)}>
                Hủy
              </Button>
              <Button variant="danger" onClick={handleReject} disabled={Boolean(processingId)}>
                {processingId ? 'Đang từ chối...' : 'Xác nhận từ chối'}
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Modal xác nhận Hủy giá đã lên lịch */}
      {cancelModalPriceId && (
        <Modal title="Hủy giá bán đã lên lịch" onClose={() => setCancelModalPriceId(null)}>
          <div style={{ padding: '8px 12px' }}>
            <p style={{ fontSize: '12px', color: '#475569', marginBottom: '10px' }}>
              Nhập lý do hủy dòng giá đã lên lịch trong tương lai này (dòng giá sẽ chuyển sang trạng thái Đã hủy và không có hiệu lực):
            </p>
            <Input
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Ví dụ: Hủy kế hoạch tăng giá mùa vụ..."
              maxLength={500}
            />
            <div className="modal-actions" style={{ marginTop: '16px' }}>
              <Button variant="secondary" onClick={() => setCancelModalPriceId(null)}>
                Hủy
              </Button>
              <Button variant="danger" onClick={handleCancelScheduled} disabled={Boolean(processingId)}>
                {processingId ? 'Đang hủy...' : 'Xác nhận hủy giá'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </Modal>
  );
};
