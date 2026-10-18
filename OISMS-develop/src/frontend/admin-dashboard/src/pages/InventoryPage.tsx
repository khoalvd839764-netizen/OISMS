import {
  AlertCircle,
  ArrowDownToLine,
  BookOpen,
  Building2,
  CheckCircle2,
  ClipboardList,
  Plus,
  RefreshCw,
  Search,
  Settings2,
  Trash2,
  TriangleAlert,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { Badge, Button, EmptyState, Input, Modal, PageTitle, Select } from '../components/common/ui';
import { TableSkeleton } from '../components/feedback/Skeleton';
import { useToast } from '../components/feedback/Toast';
import { useAuth } from '../context/AuthContext';
import { catalogApi } from '../api/catalog.api';
import { organizationApi } from '../api/organization.api';
import { inventoryApi } from '../api/inventory.api';
import { parseApiError } from '../api/client';
import type { ProductResponse } from '../types/catalog.types';
import type { SupplierResponse, WarehouseResponse } from '../types/organization.types';
import type {
  CreateGoodsReceiptLineRequest,
  GoodsReceiptResponse,
  InventoryBalanceDto,
  InventoryLedgerEntryDto,
} from '../types/inventory.types';

type InventoryTab = 'balances' | 'receipts' | 'ledger';

interface SkuOption {
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  retailPrice: number;
}

export function InventoryPage() {
  const { hasRole } = useAuth();
  const { showToast } = useToast();
  const canManage = hasRole(['Owner', 'Staff']);

  const [activeTab, setActiveTab] = useState<InventoryTab>('balances');

  // Master data from live backend endpoints
  const [warehouses, setWarehouses] = useState<WarehouseResponse[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierResponse[]>([]);
  const [products, setProducts] = useState<ProductResponse[]>([]);

  // Inventory data
  const [balances, setBalances] = useState<InventoryBalanceDto[]>([]);
  const [receipts, setReceipts] = useState<GoodsReceiptResponse[]>([]);
  const [ledgerEntries, setLedgerEntries] = useState<InventoryLedgerEntryDto[]>([]);

  // Loading & error states
  const [loading, setLoading] = useState(true);
  const [apiNotice, setApiNotice] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Filters
  const [searchQuery, setSearchQuery] = useState('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [lowStockOnly, setLowStockOnly] = useState(false);
  const [directionFilter, setDirectionFilter] = useState<'' | 'IN' | 'OUT'>('');
  const [operationFilter, setOperationFilter] = useState('');

  // Flatten all SKU variants from products for the Multi-line Goods Receipt selector
  const skuOptions = useMemo<SkuOption[]>(() => {
    const list: SkuOption[] = [];
    for (const p of products) {
      for (const v of p.variants || []) {
        list.push({
          skuId: v.id,
          skuCode: v.sku,
          productName: p.name,
          variantName: v.variantName,
          retailPrice: v.retailPrice,
        });
      }
    }
    return list;
  }, [products]);

  // Load master data (Warehouses, Suppliers, Products)
  const loadMasterData = useCallback(async () => {
    try {
      const [whData, supData, prodData] = await Promise.all([
        organizationApi.getWarehouses(),
        organizationApi.getSuppliers(),
        catalogApi.getProducts(),
      ]);
      setWarehouses(whData);
      setSuppliers(supData);
      setProducts(prodData);
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi tải dữ liệu danh mục kho / nhà cung cấp', parsed.message);
    }
  }, [showToast]);

  // Load inventory balances, receipts, and ledger
  const loadInventoryData = useCallback(async () => {
    setLoading(true);
    setApiNotice(null);
    try {
      const [balRes, recRes, ledRes] = await Promise.allSettled([
        inventoryApi.getBalances({
          warehouseId: warehouseFilter || undefined,
          lowStockOnly: lowStockOnly || undefined,
        }),
        inventoryApi.getReceipts({
          warehouseId: warehouseFilter || undefined,
        }),
        inventoryApi.getLedger({
          warehouseId: warehouseFilter || undefined,
          direction: directionFilter || undefined,
          operationType: operationFilter || undefined,
        }),
      ]);

      if (balRes.status === 'fulfilled') {
        setBalances(balRes.value);
      } else {
        setBalances([]);
      }

      if (recRes.status === 'fulfilled') {
        setReceipts(recRes.value);
      } else {
        setReceipts([]);
      }

      if (ledRes.status === 'fulfilled') {
        setLedgerEntries(ledRes.value);
      } else {
        setLedgerEntries([]);
      }

      if (
        balRes.status === 'rejected' &&
        recRes.status === 'rejected' &&
        ledRes.status === 'rejected'
      ) {
        const parsed = parseApiError(balRes.reason);
        setApiNotice(parsed.message);
      }
    } finally {
      setLoading(false);
    }
  }, [warehouseFilter, lowStockOnly, directionFilter, operationFilter]);

  useEffect(() => {
    void loadMasterData();
  }, [loadMasterData]);

  useEffect(() => {
    void loadInventoryData();
  }, [loadInventoryData]);

  // Compute low-stock items
  const lowStockBalances = useMemo(
    () => balances.filter((b) => b.onHand - b.reserved <= b.reorderPoint),
    [balances]
  );

  // Filtered balances by search query
  const filteredBalances = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return balances.filter((b) => {
      if (lowStockOnly && b.onHand - b.reserved > b.reorderPoint) return false;
      if (!q) return true;
      return (
        b.skuCode.toLowerCase().includes(q) ||
        b.productName.toLowerCase().includes(q) ||
        b.variantName.toLowerCase().includes(q)
      );
    });
  }, [balances, searchQuery, lowStockOnly]);

  // Filtered ledger entries by search query
  const filteredLedger = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return ledgerEntries.filter((entry) => {
      if (!q) return true;
      return (
        entry.skuCode.toLowerCase().includes(q) ||
        entry.referenceCode.toLowerCase().includes(q) ||
        entry.productName.toLowerCase().includes(q)
      );
    });
  }, [ledgerEntries, searchQuery]);

  // ============================================================================
  // MULTI-LINE GOODS RECEIPT MODAL (PHIẾU NHẬP KHO NHIỀU DÒNG)
  // ============================================================================
  const [receiptModalOpen, setReceiptModalOpen] = useState(false);
  const [receiptWarehouseId, setReceiptWarehouseId] = useState('');
  const [receiptSupplierId, setReceiptSupplierId] = useState('');
  const [receiptNote, setReceiptNote] = useState('');
  const [receiptLines, setReceiptLines] = useState<CreateGoodsReceiptLineRequest[]>([
    { skuId: '', quantity: 10, unitCost: 100000 },
  ]);

  const openReceiptModal = () => {
    setReceiptWarehouseId(warehouses.find((w) => w.isActive)?.id || warehouses[0]?.id || '');
    setReceiptSupplierId(suppliers[0]?.id || '');
    setReceiptNote('');
    setReceiptLines([
      {
        skuId: skuOptions[0]?.skuId || '',
        quantity: 10,
        unitCost: 100000,
      },
    ]);
    setReceiptModalOpen(true);
  };

  const addReceiptLine = () => {
    setReceiptLines((prev) => [
      ...prev,
      {
        skuId: skuOptions[0]?.skuId || '',
        quantity: 1,
        unitCost: 100000,
      },
    ]);
  };

  const removeReceiptLine = (idx: number) => {
    if (receiptLines.length <= 1) return;
    setReceiptLines((prev) => prev.filter((_, i) => i !== idx));
  };

  const updateReceiptLine = <K extends keyof CreateGoodsReceiptLineRequest>(
    idx: number,
    key: K,
    value: CreateGoodsReceiptLineRequest[K]
  ) => {
    setReceiptLines((prev) =>
      prev.map((line, i) => (i === idx ? { ...line, [key]: value } : line))
    );
  };

  const totalReceiptAmount = useMemo(
    () => receiptLines.reduce((sum, l) => sum + Number(l.quantity || 0) * Number(l.unitCost || 0), 0),
    [receiptLines]
  );

  const totalReceiptQty = useMemo(
    () => receiptLines.reduce((sum, l) => sum + Number(l.quantity || 0), 0),
    [receiptLines]
  );

  // Calculate WAC preview for a line based on existing balance in selected warehouse
  const getLineWacPreview = (line: CreateGoodsReceiptLineRequest) => {
    const existing = balances.find(
      (b) => b.warehouseId === receiptWarehouseId && b.skuId === line.skuId
    );
    const oldQty = existing?.onHand || 0;
    const oldWac = existing?.averageCost || 0;
    const inQty = Number(line.quantity) || 0;
    const inCost = Number(line.unitCost) || 0;
    const newQty = oldQty + inQty;
    const newWac = newQty > 0 ? (oldQty * oldWac + inQty * inCost) / newQty : inCost;
    return { oldQty, oldWac, newQty, newWac };
  };

  const handleCreateReceipt = async (confirmImmediately: boolean) => {
    if (!receiptWarehouseId) {
      showToast('warning', 'Chưa chọn kho nhận', 'Vui lòng chọn kho nhập hàng.');
      return;
    }
    if (!receiptSupplierId) {
      showToast('warning', 'Chưa chọn nhà cung cấp', 'Vui lòng chọn hoặc tạo nhà cung cấp.');
      return;
    }

    for (let i = 0; i < receiptLines.length; i++) {
      const l = receiptLines[i];
      if (!l.skuId) {
        showToast('warning', 'Thiếu SKU', `Vui lòng chọn SKU tại dòng #${i + 1}.`);
        return;
      }
      if (l.quantity <= 0) {
        showToast('warning', 'Số lượng không hợp lệ', `Số lượng nhập tại dòng #${i + 1} phải lớn hơn 0.`);
        return;
      }
      if (l.unitCost < 0) {
        showToast('warning', 'Đơn giá âm', `Đơn giá nhập tại dòng #${i + 1} không được âm.`);
        return;
      }
    }

    setSubmitting(true);
    try {
      const created = await inventoryApi.createReceipt({
        warehouseId: receiptWarehouseId,
        supplierId: receiptSupplierId,
        note: receiptNote.trim() || null,
        lines: receiptLines.map((l) => ({
          skuId: l.skuId,
          quantity: Number(l.quantity),
          unitCost: Number(l.unitCost),
        })),
      });

      if (confirmImmediately && created.id) {
        await inventoryApi.confirmReceipt(created.id);
        showToast(
          'success',
          'Đã xác nhận & ghi sổ phiếu nhập kho',
          `Phiếu ${created.code} đã cộng tồn kho và tính lại giá vốn bình quân (WAC).`
        );
      } else {
        showToast('success', 'Đã lưu phiếu nhập nháp', `Mã phiếu: ${created.code}`);
      }

      setReceiptModalOpen(false);
      await loadInventoryData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể tạo phiếu nhập kho', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleConfirmExistingReceipt = async (receipt: GoodsReceiptResponse) => {
    if (
      !window.confirm(
        `Xác nhận và ghi sổ phiếu nhập "${receipt.code}"? Sau khi xác nhận, phiếu sẽ khóa sửa và tạo bút toán IN vào Sổ kho.`
      )
    ) {
      return;
    }
    try {
      await inventoryApi.confirmReceipt(receipt.id);
      showToast('success', 'Đã ghi sổ phiếu nhập', `Phiếu ${receipt.code} đã được xác nhận.`);
      await loadInventoryData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể xác nhận phiếu nhập', parsed.message);
    }
  };

  // ============================================================================
  // QUICK CREATE SUPPLIER MODAL
  // ============================================================================
  const [supplierModalOpen, setSupplierModalOpen] = useState(false);
  const [supName, setSupName] = useState('');
  const [supCode, setSupCode] = useState('');
  const [supContact, setSupContact] = useState('');
  const [supPhone, setSupPhone] = useState('');
  const [supEmail, setSupEmail] = useState('');
  const [supAddress, setSupAddress] = useState('');

  const handleCreateSupplier = async (e: FormEvent) => {
    e.preventDefault();
    if (!supName.trim()) {
      showToast('warning', 'Thiếu tên nhà cung cấp', 'Vui lòng nhập tên nhà cung cấp.');
      return;
    }
    setSubmitting(true);
    try {
      const created = await organizationApi.createSupplier({
        name: supName.trim(),
        code: supCode.trim() || null,
        contactName: supContact.trim() || null,
        phone: supPhone.trim() || null,
        email: supEmail.trim() || null,
        address: supAddress.trim() || null,
      });
      showToast('success', 'Đã thêm nhà cung cấp', `Nhà cung cấp "${created.name}" đã được tạo.`);
      setSuppliers((prev) => [created, ...prev]);
      setReceiptSupplierId(created.id);
      setSupplierModalOpen(false);
      setSupName('');
      setSupCode('');
      setSupContact('');
      setSupPhone('');
      setSupEmail('');
      setSupAddress('');
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể tạo nhà cung cấp', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  // ============================================================================
  // REORDER POINT (THRESHOLD) MODAL & LEDGER VOUCHER MODAL
  // ============================================================================
  const [editingThresholdItem, setEditingThresholdItem] = useState<InventoryBalanceDto | null>(null);
  const [newReorderPoint, setNewReorderPoint] = useState(5);
  const [selectedLedgerEntry, setSelectedLedgerEntry] = useState<InventoryLedgerEntryDto | null>(null);

  const handleUpdateThreshold = async (e: FormEvent) => {
    e.preventDefault();
    if (!editingThresholdItem) return;
    setSubmitting(true);
    try {
      await inventoryApi.updateReorderPoint({
        warehouseId: editingThresholdItem.warehouseId,
        skuId: editingThresholdItem.skuId,
        reorderPoint: Number(newReorderPoint),
      });
      showToast(
        'success',
        'Đã cập nhật ngưỡng cảnh báo tồn',
        `SKU ${editingThresholdItem.skuCode} có ngưỡng cảnh báo mới: ${newReorderPoint}.`
      );
      setEditingThresholdItem(null);
      await loadInventoryData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể lưu ngưỡng tồn', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <PageTitle
        eyebrow="FR-INV · FR-COST · WAC ENGINE"
        title="Quản lý Tồn kho, Phiếu nhập & Sổ kho bất biến"
        description="Theo dõi tồn thực tế (OnHand), giữ chỗ (Reserved), khả dụng (Available), giá vốn bình quân gia quyền (WAC) và sổ cái kho chỉ đọc."
        actions={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                void loadMasterData();
                void loadInventoryData();
              }}
            >
              <RefreshCw size={16} /> Làm mới
            </Button>
            {canManage && (
              <>
                <Button variant="secondary" onClick={() => setSupplierModalOpen(true)}>
                  <Building2 size={16} /> + Nhà cung cấp
                </Button>
                <Button onClick={openReceiptModal}>
                  <Plus size={17} /> Tạo phiếu nhập kho nhiều dòng
                </Button>
              </>
            )}
          </>
        }
      />

      {/* Process KPI Strip */}
      <div className="process-strip">
        <div onClick={() => setActiveTab('balances')} style={{ cursor: 'pointer' }}>
          <span>
            <ClipboardList size={18} />
          </span>
          <strong>Kho vật lý & Nhà cung cấp</strong>
          <small>
            {warehouses.length} kho · {suppliers.length} nhà cung cấp
          </small>
        </div>
        <div onClick={() => setActiveTab('receipts')} style={{ cursor: 'pointer' }}>
          <span>
            <ArrowDownToLine size={18} />
          </span>
          <strong>Phiếu nhập kho</strong>
          <small>
            {receipts.filter((r) => r.status === 'Draft').length} nháp / {receipts.length} tổng phiếu
          </small>
        </div>
        <div onClick={() => setActiveTab('ledger')} style={{ cursor: 'pointer' }}>
          <span>
            <BookOpen size={18} />
          </span>
          <strong>Sổ kho bất biến (Ledger)</strong>
          <small>{ledgerEntries.length} bút toán IN / OUT</small>
        </div>
        <div
          onClick={() => {
            setActiveTab('balances');
            setLowStockOnly(true);
          }}
          style={{ cursor: 'pointer' }}
        >
          <span>
            <TriangleAlert size={18} />
          </span>
          <strong>Cảnh báo sắp hết hàng</strong>
          <small>{lowStockBalances.length} SKU dưới ngưỡng</small>
        </div>
      </div>

      {/* Low Stock Alert Panel */}
      {lowStockBalances.length > 0 && (
        <section className="low-stock-panel">
          <div>
            <TriangleAlert size={19} />
            <span>
              <strong>Cảnh báo tồn khả dụng chạm ngưỡng (Available ≤ ReorderPoint)</strong>
              <small>Cần lập phiếu nhập kho bổ sung để tránh đứt gãy đơn hàng đa kênh</small>
            </span>
          </div>
          <div className="low-stock-links">
            {lowStockBalances.slice(0, 5).map((item) => (
              <button
                key={`${item.warehouseId}-${item.skuId}`}
                type="button"
                onClick={() => {
                  setEditingThresholdItem(item);
                  setNewReorderPoint(item.reorderPoint);
                }}
              >
                <strong>{item.skuCode}</strong>
                <span>
                  {item.warehouseName} · Khả dụng {item.onHand - item.reserved} / Ngưỡng {item.reorderPoint}
                </span>
              </button>
            ))}
          </div>
          <Button variant="secondary" onClick={openReceiptModal}>
            <Plus size={16} /> Nhập bổ sung ngay
          </Button>
        </section>
      )}

      {apiNotice && (
        <div
          role="status"
          style={{
            marginBottom: '16px',
            padding: '12px 16px',
            borderRadius: '10px',
            background: '#FFFBEB',
            border: '1px solid #FDE68A',
            color: '#92400E',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '12px',
            fontSize: '13px',
          }}
        >
          <span style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <AlertCircle size={18} />
            <span>
              <strong>Trạng thái kết nối API Kho (`/api/inventory/*`):</strong> {apiNotice}. Danh sách Kho ({warehouses.length}), Nhà cung cấp ({suppliers.length}) và SKU ({skuOptions.length}) đã sẵn sàng từ Backend.
            </span>
          </span>
          <Button variant="secondary" onClick={() => void loadInventoryData()}>
            Thử lại
          </Button>
        </div>
      )}

      <section className="card">
        <div className="tabs">
          <button
            type="button"
            className={activeTab === 'balances' ? 'active' : ''}
            onClick={() => setActiveTab('balances')}
          >
            Tồn kho theo SKU & Kho ({filteredBalances.length})
          </button>
          <button
            type="button"
            className={activeTab === 'receipts' ? 'active' : ''}
            onClick={() => setActiveTab('receipts')}
          >
            Phiếu nhập kho nhiều dòng ({receipts.length})
          </button>
          <button
            type="button"
            className={activeTab === 'ledger' ? 'active' : ''}
            onClick={() => setActiveTab('ledger')}
          >
            Sổ kho chỉ đọc (Append-Only Ledger) ({filteredLedger.length})
          </button>
        </div>

        {/* ===================================================================
            TAB 1: INVENTORY BALANCES
        =================================================================== */}
        {activeTab === 'balances' && (
          <>
            <div className="toolbar">
              <label className="search-field">
                <Search size={18} />
                <Input
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Tìm theo mã SKU, tên sản phẩm hoặc biến thể..."
                />
              </label>

              <Select
                aria-label="Lọc theo kho"
                value={warehouseFilter}
                onChange={(e) => setWarehouseFilter(e.target.value)}
              >
                <option value="">Tất cả kho vật lý ({warehouses.length})</option>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name} ({w.branchName})
                  </option>
                ))}
              </Select>

              <Select
                aria-label="Lọc cảnh báo"
                value={lowStockOnly ? 'low' : 'all'}
                onChange={(e) => setLowStockOnly(e.target.value === 'low')}
              >
                <option value="all">Tất cả mức tồn kho</option>
                <option value="low">Chỉ SKU chạm ngưỡng cảnh báo</option>
              </Select>
            </div>

            {loading ? (
              <div style={{ padding: '16px' }}>
                <TableSkeleton rows={5} columns={8} />
              </div>
            ) : filteredBalances.length === 0 ? (
              <EmptyState
                title="Chưa có dữ liệu tồn kho ghi nhận"
                description="Tồn kho chỉ được khởi tạo và cộng thêm thông qua nghiệp vụ Xác nhận Phiếu nhập kho (Goods Receipt)."
                action={
                  canManage ? (
                    <Button onClick={openReceiptModal}>
                      <Plus size={16} /> Tạo phiếu nhập kho đầu tiên
                    </Button>
                  ) : undefined
                }
              />
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>SKU / Sản phẩm</th>
                      <th>Chi nhánh / Kho</th>
                      <th>Tồn thực tế (OnHand)</th>
                      <th>Giữ chỗ (Reserved)</th>
                      <th>Khả dụng (Available)</th>
                      <th>Giá vốn BQ (WAC)</th>
                      <th>Tổng giá trị tồn</th>
                      <th>Ngưỡng cảnh báo</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredBalances.map((item) => {
                      const available = item.onHand - item.reserved;
                      const isLow = available <= item.reorderPoint;
                      return (
                        <tr key={`${item.warehouseId}-${item.skuId}`}>
                          <td>
                            <div className="product-cell">
                              <span className="product-thumb">
                                {item.skuCode.slice(0, 2).toUpperCase()}
                              </span>
                              <div>
                                <strong className="mono">{item.skuCode}</strong>
                                <span>
                                  {item.productName} · {item.variantName}
                                </span>
                              </div>
                            </div>
                          </td>
                          <td>
                            <strong>{item.warehouseName}</strong>
                            <div className="muted" style={{ fontSize: '12px' }}>
                              {item.branchName}
                            </div>
                          </td>
                          <td>
                            <strong>{item.onHand}</strong>
                          </td>
                          <td>{item.reserved}</td>
                          <td>
                            <strong className={isLow ? 'text-warning' : ''}>{available}</strong>
                          </td>
                          <td>{item.averageCost.toLocaleString('vi-VN')} ₫</td>
                          <td>
                            <strong>{(item.onHand * item.averageCost).toLocaleString('vi-VN')} ₫</strong>
                          </td>
                          <td>
                            <Button
                              variant="ghost"
                              onClick={() => {
                                setEditingThresholdItem(item);
                                setNewReorderPoint(item.reorderPoint);
                              }}
                              title="Cấu hình ngưỡng cảnh báo"
                            >
                              <Settings2 size={14} /> {item.reorderPoint}
                            </Button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}

        {/* ===================================================================
            TAB 2: MULTI-LINE GOODS RECEIPTS
        =================================================================== */}
        {activeTab === 'receipts' && (
          <>
            <div className="toolbar">
              <Select
                value={warehouseFilter}
                onChange={(e) => setWarehouseFilter(e.target.value)}
              >
                <option value="">Tất cả kho nhận</option>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name} ({w.branchName})
                  </option>
                ))}
              </Select>
              {canManage && (
                <Button onClick={openReceiptModal}>
                  <Plus size={16} /> Tạo phiếu nhập nhiều dòng
                </Button>
              )}
            </div>

            {loading ? (
              <div style={{ padding: '16px' }}>
                <TableSkeleton rows={4} columns={7} />
              </div>
            ) : receipts.length === 0 ? (
              <EmptyState
                title="Chưa có phiếu nhập kho nào"
                description="Tạo phiếu nhập kho nhiều dòng để ghi nhận hàng về kho và tính giá vốn bình quân (WAC)."
                action={
                  canManage ? (
                    <Button onClick={openReceiptModal}>
                      <Plus size={16} /> Lập phiếu nhập kho
                    </Button>
                  ) : undefined
                }
              />
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Mã phiếu</th>
                      <th>Kho nhận</th>
                      <th>Nhà cung cấp</th>
                      <th>Số dòng SKU</th>
                      <th>Tổng giá trị nhập</th>
                      <th>Trạng thái</th>
                      <th>Thời gian tạo</th>
                      {canManage && <th>Thao tác</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {receipts.map((r) => (
                      <tr key={r.id}>
                        <td>
                          <strong className="mono">{r.code}</strong>
                        </td>
                        <td>{r.warehouseName}</td>
                        <td>{r.supplierName}</td>
                        <td>{r.lines?.length || 0} dòng</td>
                        <td>
                          <strong>{r.totalAmount.toLocaleString('vi-VN')} ₫</strong>
                        </td>
                        <td>
                          <Badge
                            tone={
                              r.status === 'Confirmed'
                                ? 'success'
                                : r.status === 'Draft'
                                ? 'warning'
                                : 'neutral'
                            }
                          >
                            {r.status === 'Confirmed'
                              ? 'Đã ghi sổ'
                              : r.status === 'Draft'
                              ? 'Bản nháp'
                              : 'Đã hủy'}
                          </Badge>
                        </td>
                        <td>{new Date(r.createdAt).toLocaleString('vi-VN')}</td>
                        {canManage && (
                          <td>
                            {r.status === 'Draft' && (
                              <Button
                                variant="secondary"
                                onClick={() => void handleConfirmExistingReceipt(r)}
                              >
                                <CheckCircle2 size={15} /> Xác nhận & Ghi sổ
                              </Button>
                            )}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}

        {/* ===================================================================
            TAB 3: READ-ONLY APPEND-ONLY INVENTORY LEDGER
        =================================================================== */}
        {activeTab === 'ledger' && (
          <>
            <div className="toolbar ledger-filters">
              <label className="search-field">
                <Search size={18} />
                <Input
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Tìm theo mã SKU hoặc mã chứng từ gốc..."
                />
              </label>

              <Select
                value={warehouseFilter}
                onChange={(e) => setWarehouseFilter(e.target.value)}
              >
                <option value="">Tất cả kho</option>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </Select>

              <Select
                value={directionFilter}
                onChange={(e) => setDirectionFilter(e.target.value as '' | 'IN' | 'OUT')}
              >
                <option value="">Cả IN và OUT</option>
                <option value="IN">IN (Nhập kho)</option>
                <option value="OUT">OUT (Xuất kho)</option>
              </Select>

              <Select
                value={operationFilter}
                onChange={(e) => setOperationFilter(e.target.value)}
              >
                <option value="">Tất cả nghiệp vụ</option>
                <option value="GoodsReceipt">Nhập hàng (GoodsReceipt)</option>
                <option value="SaleOut">Xuất bán (SaleOut)</option>
                <option value="TransferIn">Chuyển kho đến (TransferIn)</option>
                <option value="TransferOut">Chuyển kho đi (TransferOut)</option>
                <option value="Adjustment">Kiểm kê điều chỉnh (Adjustment)</option>
                <option value="ReturnIn">Khách trả hàng (ReturnIn)</option>
              </Select>
            </div>

            {loading ? (
              <div style={{ padding: '16px' }}>
                <TableSkeleton rows={5} columns={8} />
              </div>
            ) : filteredLedger.length === 0 ? (
              <EmptyState
                title="Sổ kho chưa có bút toán nào"
                description="Mọi biến động nhập/xuất/chuyển/kiểm kê sau khi xác nhận sẽ tự động sinh bút toán bất biến tại đây."
              />
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Thời gian ghi sổ</th>
                      <th>Kho</th>
                      <th>Mã SKU</th>
                      <th>Chiều</th>
                      <th>Nghiệp vụ</th>
                      <th>Số lượng</th>
                      <th>Tồn sau giao dịch</th>
                      <th>Chứng từ gốc</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredLedger.map((entry) => (
                      <tr key={entry.id}>
                        <td>{new Date(entry.createdAt).toLocaleString('vi-VN')}</td>
                        <td>{entry.warehouseName}</td>
                        <td>
                          <strong className="mono">{entry.skuCode}</strong>
                          <div className="muted" style={{ fontSize: '12px' }}>
                            {entry.productName}
                          </div>
                        </td>
                        <td>
                          <Badge tone={entry.direction === 'IN' ? 'success' : 'danger'}>
                            {entry.direction}
                          </Badge>
                        </td>
                        <td>{entry.operationType}</td>
                        <td>
                          <strong>
                            {entry.direction === 'IN' ? '+' : '−'}
                            {entry.quantityDelta}
                          </strong>
                        </td>
                        <td>
                          <strong>{entry.balanceAfter}</strong>
                        </td>
                        <td>
                          <Button
                            variant="ghost"
                            className="text-link"
                            onClick={() => setSelectedLedgerEntry(entry)}
                          >
                            {entry.referenceCode}
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="readonly-note" style={{ margin: '16px' }}>
              <BookOpen size={17} />
              <span>
                <strong>Nguyên tắc Sổ kho bất biến (Append-Only Inventory Ledger):</strong> Sổ kho chỉ đọc, tuyệt đối không có nút Sửa hoặc Xóa bút toán quá khứ và không cho phép sửa trực tiếp trường <code>onHand</code>. Mọi sai lệch kiểm kê phải được xử lý bằng chứng từ điều chỉnh mới.
              </span>
            </div>
          </>
        )}
      </section>

      {/* =====================================================================
          MODAL 1: MULTI-LINE GOODS RECEIPT (PHIẾU NHẬP KHO NHIỀU DÒNG)
      ===================================================================== */}
      {receiptModalOpen && (
        <Modal
          title="Lập Phiếu nhập kho nhiều dòng (Multi-line Goods Receipt)"
          onClose={() => setReceiptModalOpen(false)}
          wide
        >
          <div className="form-grid">
            <label>
              <span>Kho nhận hàng *</span>
              <Select
                value={receiptWarehouseId}
                onChange={(e) => setReceiptWarehouseId(e.target.value)}
                required
              >
                <option value="">-- Chọn kho vật lý --</option>
                {warehouses
                  .filter((w) => w.isActive)
                  .map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.name} ({w.branchName})
                    </option>
                  ))}
              </Select>
            </label>

            <label>
              <span>Nhà cung cấp *</span>
              <div className="inline-input">
                <Select
                  value={receiptSupplierId}
                  onChange={(e) => setReceiptSupplierId(e.target.value)}
                  required
                >
                  <option value="">-- Chọn nhà cung cấp --</option>
                  {suppliers.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name} ({s.code})
                    </option>
                  ))}
                </Select>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setSupplierModalOpen(true)}
                  title="Thêm nhanh nhà cung cấp mới"
                >
                  <Plus size={15} /> Mới
                </Button>
              </div>
            </label>

            <label style={{ gridColumn: '1 / -1' }}>
              <span>Ghi chú chứng từ nhập kho</span>
              <Input
                placeholder="Số hóa đơn VAT, số hợp đồng, người giao hàng..."
                value={receiptNote}
                onChange={(e) => setReceiptNote(e.target.value)}
              />
            </label>
          </div>

          <div
            style={{
              marginTop: '18px',
              marginBottom: '10px',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <strong>Chi tiết các dòng hàng nhập ({receiptLines.length} dòng SKU)</strong>
            <Button type="button" variant="secondary" onClick={addReceiptLine}>
              <Plus size={15} /> Thêm dòng SKU
            </Button>
          </div>

          <div className="table-wrap" style={{ maxHeight: '300px', overflowY: 'auto' }}>
            <table>
              <thead>
                <tr>
                  <th>Biến thể SKU *</th>
                  <th>Số lượng nhập *</th>
                  <th>Đơn giá nhập (₫) *</th>
                  <th>Thành tiền</th>
                  <th>Dự kiến WAC mới</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {receiptLines.map((line, idx) => {
                  const lineTotal = Number(line.quantity || 0) * Number(line.unitCost || 0);
                  const wacPreview = getLineWacPreview(line);
                  return (
                    <tr key={idx}>
                      <td>
                        <Select
                          value={line.skuId}
                          onChange={(e) => updateReceiptLine(idx, 'skuId', e.target.value)}
                        >
                          <option value="">-- Chọn SKU --</option>
                          {skuOptions.map((opt) => (
                            <option key={opt.skuId} value={opt.skuId}>
                              {opt.skuCode} — {opt.productName} ({opt.variantName})
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Input
                          type="number"
                          min={1}
                          value={line.quantity}
                          onChange={(e) =>
                            updateReceiptLine(idx, 'quantity', Math.max(1, Number(e.target.value)))
                          }
                        />
                      </td>
                      <td>
                        <Input
                          type="number"
                          min={0}
                          step={1000}
                          value={line.unitCost}
                          onChange={(e) =>
                            updateReceiptLine(idx, 'unitCost', Math.max(0, Number(e.target.value)))
                          }
                        />
                      </td>
                      <td>
                        <strong>{lineTotal.toLocaleString('vi-VN')} ₫</strong>
                      </td>
                      <td>
                        <span className="mono" style={{ fontSize: '12px' }}>
                          {Math.round(wacPreview.newWac).toLocaleString('vi-VN')} ₫
                        </span>
                        <div className="muted" style={{ fontSize: '11px' }}>
                          Tồn sau nhập: {wacPreview.newQty}
                        </div>
                      </td>
                      <td>
                        {receiptLines.length > 1 && (
                          <Button
                            type="button"
                            variant="ghost"
                            onClick={() => removeReceiptLine(idx)}
                            title="Xóa dòng"
                          >
                            <Trash2 size={15} />
                          </Button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="receipt-summary" style={{ marginTop: '14px' }}>
            <span>
              Tổng số dòng SKU<strong>{receiptLines.length} dòng</strong>
            </span>
            <span>
              Tổng số lượng nhập<strong>{totalReceiptQty.toLocaleString('vi-VN')} đơn vị</strong>
            </span>
            <span>
              Tổng giá trị phiếu nhập<strong>{totalReceiptAmount.toLocaleString('vi-VN')} ₫</strong>
            </span>
          </div>

          <div className="impact-box" style={{ marginTop: '12px' }}>
            <strong>Cơ chế tính Giá vốn bình quân gia quyền (WAC) & Ghi sổ kho</strong>
            <p>
              Khi nhấn <b>Xác nhận & Ghi sổ</b>, hệ thống sẽ sinh bút toán <code>IN</code> cho từng dòng SKU, tăng <code>OnHand</code> và tự động tính lại giá vốn bình quân theo công thức:{' '}
              <code>WAC_mới = (Tồn_cũ × WAC_cũ + SL_nhập × Giá_nhập) / (Tồn_cũ + SL_nhập)</code>.
            </p>
          </div>

          <div className="modal-actions">
            <Button type="button" variant="secondary" onClick={() => setReceiptModalOpen(false)}>
              Đóng
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={submitting}
              onClick={() => void handleCreateReceipt(false)}
            >
              Lưu bản nháp (Draft)
            </Button>
            <Button
              type="button"
              disabled={submitting}
              onClick={() => void handleCreateReceipt(true)}
            >
              <CheckCircle2 size={16} />
              {submitting ? 'Đang xử lý...' : 'Xác nhận & Ghi sổ ngay'}
            </Button>
          </div>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 2: QUICK CREATE SUPPLIER
      ===================================================================== */}
      {supplierModalOpen && (
        <Modal title="Thêm Nhà cung cấp mới" onClose={() => setSupplierModalOpen(false)}>
          <form onSubmit={handleCreateSupplier}>
            <div className="form-grid">
              <label>
                <span>Tên nhà cung cấp *</span>
                <Input
                  placeholder="Ví dụ: Công ty Dệt may An Phú"
                  value={supName}
                  onChange={(e) => setSupName(e.target.value)}
                  required
                />
              </label>
              <label>
                <span>Mã nhà cung cấp (Tự sinh nếu để trống)</span>
                <Input
                  placeholder="VD: SUP-ANPHU"
                  value={supCode}
                  onChange={(e) => setSupCode(e.target.value.toUpperCase())}
                />
              </label>
              <label>
                <span>Người liên hệ</span>
                <Input
                  placeholder="Nguyễn Văn B"
                  value={supContact}
                  onChange={(e) => setSupContact(e.target.value)}
                />
              </label>
              <label>
                <span>Số điện thoại</span>
                <Input
                  placeholder="0909123456"
                  value={supPhone}
                  onChange={(e) => setSupPhone(e.target.value)}
                />
              </label>
              <label>
                <span>Email</span>
                <Input
                  type="email"
                  placeholder="contact@anphu.vn"
                  value={supEmail}
                  onChange={(e) => setSupEmail(e.target.value)}
                />
              </label>
              <label>
                <span>Địa chỉ</span>
                <Input
                  placeholder="Quận Tân Bình, TP.HCM"
                  value={supAddress}
                  onChange={(e) => setSupAddress(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setSupplierModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Lưu nhà cung cấp'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 3: CONFIGURE REORDER POINT THRESHOLD
      ===================================================================== */}
      {editingThresholdItem && (
        <Modal
          title={`Cấu hình ngưỡng cảnh báo: ${editingThresholdItem.skuCode}`}
          onClose={() => setEditingThresholdItem(null)}
        >
          <form onSubmit={handleUpdateThreshold}>
            <div className="form-grid">
              <label>
                <span>Kho áp dụng</span>
                <Input value={editingThresholdItem.warehouseName} readOnly />
              </label>
              <label>
                <span>Tồn khả dụng hiện tại</span>
                <Input
                  value={String(editingThresholdItem.onHand - editingThresholdItem.reserved)}
                  readOnly
                />
              </label>
              <label style={{ gridColumn: '1 / -1' }}>
                <span>Ngưỡng cảnh báo tồn tối thiểu (Reorder Point) *</span>
                <Input
                  type="number"
                  min={0}
                  value={newReorderPoint}
                  onChange={(e) => setNewReorderPoint(Math.max(0, Number(e.target.value)))}
                  required
                />
              </label>
            </div>
            <div className="hint-box">
              Khi Tồn khả dụng (<code>OnHand - Reserved</code>) nhỏ hơn hoặc bằng ngưỡng này, hệ thống sẽ phát cảnh báo tồn kho thấp.
            </div>
            <div className="modal-actions">
              <Button
                type="button"
                variant="secondary"
                onClick={() => setEditingThresholdItem(null)}
              >
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                Lưu cấu hình
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 4: IMMUTABLE LEDGER VOUCHER DETAIL
      ===================================================================== */}
      {selectedLedgerEntry && (
        <Modal
          title={`Chứng từ gốc: ${selectedLedgerEntry.referenceCode}`}
          onClose={() => setSelectedLedgerEntry(null)}
        >
          <div className="document-detail">
            <Badge tone={selectedLedgerEntry.direction === 'IN' ? 'success' : 'danger'}>
              {selectedLedgerEntry.direction} · {selectedLedgerEntry.operationType}
            </Badge>
            <h3>{selectedLedgerEntry.productName}</h3>
            <p>
              Ghi sổ lúc {new Date(selectedLedgerEntry.createdAt).toLocaleString('vi-VN')} tại{' '}
              <strong>{selectedLedgerEntry.warehouseName}</strong>
            </p>
            <dl>
              <div>
                <dt>Mã SKU</dt>
                <dd className="mono">{selectedLedgerEntry.skuCode}</dd>
              </div>
              <div>
                <dt>Biến động</dt>
                <dd>
                  {selectedLedgerEntry.direction === 'IN' ? '+' : '−'}
                  {selectedLedgerEntry.quantityDelta}
                </dd>
              </div>
              <div>
                <dt>Tồn sau bút toán</dt>
                <dd>{selectedLedgerEntry.balanceAfter}</dd>
              </div>
            </dl>
            <div className="readonly-note">
              <BookOpen size={16} />
              <span>Bút toán sổ kho là bất biến (Append-Only) và không thể chỉnh sửa hoặc xóa.</span>
            </div>
          </div>
        </Modal>
      )}
    </>
  );
}
