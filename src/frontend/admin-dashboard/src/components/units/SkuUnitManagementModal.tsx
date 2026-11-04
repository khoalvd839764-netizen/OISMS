import { useEffect, useState } from "react";
import {
  AlertCircle,
  CheckCircle2,
  History,
  Layers,
  Plus,
  X,
} from "lucide-react";
import { Badge, Button, Input, Modal, Select } from "../ui";
import { unitService, parseUnitApiError } from "../../services/unitService";
import { SkuUnitHistoryModal } from "./SkuUnitHistoryModal";
import type { Sku } from "../../types/domain";
import type { Unit, SkuUnit, CreateSkuUnitRequest, UpdateSkuUnitRequest } from "../../types/units";

interface SkuUnitManagementModalProps {
  isOpen: boolean;
  onClose: () => void;
  sku: Sku | null;
  onUpdated?: () => void;
}

export function SkuUnitManagementModal({
  isOpen,
  onClose,
  sku,
  onUpdated,
}: SkuUnitManagementModalProps) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Danh mục đơn vị tính của tenant
  const [tenantUnits, setTenantUnits] = useState<Unit[]>([]);
  // Danh sách cấu hình đơn vị tính của SKU
  const [skuUnits, setSkuUnits] = useState<SkuUnit[]>([]);

  // Xem lịch sử hệ số
  const [historyModalUnit, setHistoryModalUnit] = useState<SkuUnit | null>(null);

  // Chế độ thêm đơn vị quy đổi mới
  const [showAddForm, setShowAddForm] = useState(false);
  const [newUnitId, setNewUnitId] = useState("");
  const [newFactor, setNewFactor] = useState<number>(1);
  const [newDecimalPlaces, setNewDecimalPlaces] = useState<number>(0);
  const [newBarcode, setNewBarcode] = useState("");
  const [newCanPurchase, setNewCanPurchase] = useState(true);
  const [newCanSell, setNewCanSell] = useState(true);
  const [newIsDefaultSale, setNewIsDefaultSale] = useState(false);

  // Chế độ sửa đơn vị quy đổi
  const [editingUnitId, setEditingUnitId] = useState<string | null>(null);
  const [editFactor, setEditFactor] = useState<number>(1);
  const [editDecimalPlaces, setEditDecimalPlaces] = useState<number>(0);
  const [editBarcode, setEditBarcode] = useState("");
  const [editCanPurchase, setEditCanPurchase] = useState(true);
  const [editCanSell, setEditCanSell] = useState(true);
  const [editIsDefaultPurchase, setEditIsDefaultPurchase] = useState(false);
  const [editIsDefaultSale, setEditIsDefaultSale] = useState(false);

  useEffect(() => {
    if (isOpen && sku) {
      loadData();
    } else {
      setShowAddForm(false);
      setEditingUnitId(null);
      setError(null);
      setSuccessMsg(null);
    }
  }, [isOpen, sku?.id]);

  const loadData = async () => {
    if (!sku) return;
    setLoading(true);
    setError(null);
    try {
      const [unitsRes, skuUnitsRes] = await Promise.all([
        unitService.getUnits(),
        unitService.getSkuUnits(sku.productId, sku.id),
      ]);
      setTenantUnits(unitsRes.filter((u) => u.isActive));
      setSkuUnits(skuUnitsRes);
    } catch (err: any) {
      const parsed = parseUnitApiError(err);
      setError(parsed.detail || "Không thể tải cấu hình đơn vị tính.");
    } finally {
      setLoading(false);
    }
  };

  const baseUnit = skuUnits.find((u) => u.isBase);
  const baseUnitSymbol = baseUnit?.unitSymbol || sku?.baseUnitCode || "đv";

  // Các đơn vị tenant khả dụng để thêm (chưa được gán cho SKU)
  const assignedUnitIds = new Set(skuUnits.map((u) => u.unitId));
  const availableUnitsForAdd = tenantUnits.filter(
    (u) => !assignedUnitIds.has(u.id)
  );

  const handleCreateSkuUnit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sku || !newUnitId) {
      setError("Vui lòng chọn đơn vị quy đổi.");
      return;
    }
    if (newFactor <= 0) {
      setError("Hệ số quy đổi phải lớn hơn 0.");
      return;
    }

    setLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const req: CreateSkuUnitRequest = {
        unitId: newUnitId,
        factorToBase: newFactor,
        canPurchase: newCanPurchase,
        canSell: newCanSell,
        isDefaultPurchase: false,
        isDefaultSale: newIsDefaultSale,
        barcode: newBarcode.trim() || null,
        quantityDecimalPlaces: newDecimalPlaces,
      };
      await unitService.createSkuUnit(sku.productId, sku.id, req);
      setSuccessMsg("Thêm đơn vị quy đổi thành công.");
      setShowAddForm(false);
      setNewUnitId("");
      setNewFactor(1);
      setNewBarcode("");
      await loadData();
      onUpdated?.();
    } catch (err: any) {
      const parsed = parseUnitApiError(err);
      setError(parsed.detail || "Không thể thêm đơn vị quy đổi.");
    } finally {
      setLoading(false);
    }
  };

  const handleStartEdit = (item: SkuUnit) => {
    setEditingUnitId(item.id);
    setEditFactor(item.factorToBase);
    setEditDecimalPlaces(item.quantityDecimalPlaces);
    setEditBarcode(item.barcode || "");
    setEditCanPurchase(item.canPurchase);
    setEditCanSell(item.canSell);
    setEditIsDefaultPurchase(item.isDefaultPurchase);
    setEditIsDefaultSale(item.isDefaultSale);
    setError(null);
    setSuccessMsg(null);
  };

  const handleSaveEdit = async (item: SkuUnit) => {
    if (!sku) return;
    if (editFactor <= 0) {
      setError("Hệ số quy đổi phải lớn hơn 0.");
      return;
    }

    setLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const req: UpdateSkuUnitRequest = {
        factorToBase: editFactor,
        quantityDecimalPlaces: editDecimalPlaces,
        barcode: editBarcode.trim() || null,
        canPurchase: editCanPurchase,
        canSell: editCanSell,
        isDefaultPurchase: editIsDefaultPurchase,
        isDefaultSale: editIsDefaultSale,
      };
      await unitService.updateSkuUnit(sku.productId, sku.id, item.id, req);
      setSuccessMsg(`Cập nhật đơn vị ${item.unitName} thành công.`);
      setEditingUnitId(null);
      await loadData();
      onUpdated?.();
    } catch (err: any) {
      const parsed = parseUnitApiError(err);
      setError(parsed.detail || "Không thể cập nhật cấu hình đơn vị tính.");
    } finally {
      setLoading(false);
    }
  };

  const handleToggleActive = async (item: SkuUnit) => {
    if (!sku) return;
    setLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      if (item.isActive) {
        // Tắt mềm đơn vị
        await unitService.deleteSkuUnit(sku.productId, sku.id, item.id);
        setSuccessMsg(`Đã tắt đơn vị quy đổi ${item.unitName}.`);
      } else {
        // Bật lại đơn vị
        await unitService.updateSkuUnit(sku.productId, sku.id, item.id, {
          isActive: true,
        });
        setSuccessMsg(`Đã kích hoạt lại đơn vị ${item.unitName}.`);
      }
      await loadData();
      onUpdated?.();
    } catch (err: any) {
      const parsed = parseUnitApiError(err);
      setError(parsed.detail || "Không thể thay đổi trạng thái đơn vị tính.");
    } finally {
      setLoading(false);
    }
  };

  if (!isOpen || !sku) return null;

  return (
    <>
      <Modal
        onClose={onClose}
        title={`Cấu hình Đơn vị tính & Quy cách — ${sku.code}`}
      >
        <div className="space-y-4 text-sm text-slate-700 max-h-[78vh] overflow-y-auto pr-1">
          {/* Header Info */}
          <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg flex items-center justify-between">
            <div>
              <div className="font-semibold text-slate-900">{sku.name}</div>
              <div className="text-xs text-slate-500">
                Biến thể: <span className="font-medium text-slate-700">{sku.variant || "Mặc định"}</span> | SKU:{" "}
                <span className="font-mono text-slate-700">{sku.code}</span>
              </div>
            </div>
            <div className="text-right">
              <span className="text-xs text-slate-500 block">Đơn vị gốc (tồn & WAC):</span>
              <span className="inline-flex items-center gap-1 font-bold text-indigo-700 bg-indigo-50 px-2.5 py-1 rounded border border-indigo-200">
                <Layers size={14} />
                {baseUnit ? `${baseUnit.unitName} (${baseUnit.unitSymbol})` : sku.baseUnitName || sku.baseUnitCode || "Chưa gán"}
              </span>
            </div>
          </div>

          {error && (
            <div className="p-3 bg-rose-50 border border-rose-200 text-rose-700 rounded-lg text-xs flex items-start gap-2">
              <AlertCircle size={16} className="mt-0.5 shrink-0" />
              <div>{error}</div>
            </div>
          )}

          {successMsg && (
            <div className="p-3 bg-emerald-50 border border-emerald-200 text-emerald-700 rounded-lg text-xs flex items-center gap-2">
              <CheckCircle2 size={16} className="shrink-0" />
              <div>{successMsg}</div>
            </div>
          )}

          {/* Danh sách đơn vị tính hiện có */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <h4 className="font-semibold text-slate-900 text-xs uppercase tracking-wider">
                Bảng quy cách đơn vị tính ({skuUnits.length})
              </h4>
              {!showAddForm && availableUnitsForAdd.length > 0 && (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setShowAddForm(true);
                    setNewUnitId(availableUnitsForAdd[0]?.id || "");
                    setNewFactor(1);
                    setNewDecimalPlaces(Number(availableUnitsForAdd[0]?.decimalPlaces) || 0);
                  }}
                >
                  <Plus size={14} className="mr-1 inline" /> Thêm quy cách
                </Button>
              )}
            </div>

            {loading && skuUnits.length === 0 ? (
              <div className="py-8 text-center text-slate-400">Đang tải cấu hình đơn vị...</div>
            ) : skuUnits.length === 0 ? (
              <div className="py-6 text-center text-slate-400 border border-dashed rounded-lg">
                Chưa có đơn vị tính nào cho biến thể này.
              </div>
            ) : (
              <div className="border border-slate-200 rounded-lg overflow-hidden">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-100 text-slate-600 font-semibold border-b border-slate-200">
                    <tr>
                      <th className="p-2.5">Đơn vị</th>
                      <th className="p-2.5">Loại</th>
                      <th className="p-2.5">Hệ số quy đổi</th>
                      <th className="p-2.5">Mã vạch quy cách</th>
                      <th className="p-2.5">Chức năng</th>
                      <th className="p-2.5">Trạng thái</th>
                      <th className="p-2.5 text-right">Thao tác</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-200">
                    {skuUnits.map((u) => {
                      const isEditing = editingUnitId === u.id;
                      return (
                        <tr
                          key={u.id}
                          className={`${u.isBase ? "bg-indigo-50/40" : "hover:bg-slate-50"} ${
                            !u.isActive ? "opacity-60 bg-slate-50" : ""
                          }`}
                        >
                          <td className="p-2.5 font-medium">
                            <div className="font-semibold text-slate-900">
                              {u.unitName} <span className="text-slate-500 font-normal">({u.unitSymbol})</span>
                            </div>
                            <div className="text-[10px] text-slate-400">{u.unitCode}</div>
                          </td>

                          <td className="p-2.5">
                            {u.isBase ? (
                              <Badge tone="info">Đơn vị gốc</Badge>
                            ) : (
                              <Badge tone="neutral">Quy đổi</Badge>
                            )}
                          </td>

                          <td className="p-2.5">
                            {isEditing ? (
                              <div className="space-y-1 max-w-[140px]">
                                <Input
                                  type="number"
                                  min="0.000001"
                                  step="any"
                                  value={editFactor}
                                  onChange={(e) => setEditFactor(Number(e.target.value))}
                                  disabled={u.isBase}
                                />
                                <span className="text-[10px] text-slate-500">
                                  1 {u.unitSymbol} = {editFactor} {baseUnitSymbol}
                                </span>
                              </div>
                            ) : (
                              <div>
                                <span className="font-bold text-slate-800">
                                  1 {u.unitSymbol}
                                </span>{" "}
                                ={" "}
                                <span className="font-bold text-indigo-600">
                                  {Number(u.factorToBase).toLocaleString("vi-VN")} {baseUnitSymbol}
                                </span>
                              </div>
                            )}
                          </td>

                          <td className="p-2.5">
                            {isEditing ? (
                              <Input
                                value={editBarcode}
                                onChange={(e) => setEditBarcode(e.target.value)}
                                placeholder="Barcode..."
                              />
                            ) : (
                              <span className="font-mono text-slate-600">
                                {u.barcode || "—"}
                              </span>
                            )}
                          </td>

                          <td className="p-2.5">
                            {isEditing ? (
                              <div className="space-y-1 text-[11px]">
                                <label className="flex items-center gap-1">
                                  <input
                                    type="checkbox"
                                    checked={editCanPurchase}
                                    onChange={(e) => setEditCanPurchase(e.target.checked)}
                                  />{" "}
                                  Mua
                                </label>
                                <label className="flex items-center gap-1">
                                  <input
                                    type="checkbox"
                                    checked={editCanSell}
                                    onChange={(e) => setEditCanSell(e.target.checked)}
                                  />{" "}
                                  Bán
                                </label>
                              </div>
                            ) : (
                              <div className="flex flex-wrap gap-1">
                                {u.canPurchase && (
                                  <span className="px-1.5 py-0.5 bg-blue-50 text-blue-700 rounded text-[10px]">
                                    Mua {u.isDefaultPurchase ? "(mặc định)" : ""}
                                  </span>
                                )}
                                {u.canSell && (
                                  <span className="px-1.5 py-0.5 bg-emerald-50 text-emerald-700 rounded text-[10px]">
                                    Bán {u.isDefaultSale ? "(mặc định)" : ""}
                                  </span>
                                )}
                              </div>
                            )}
                          </td>

                          <td className="p-2.5">
                            {u.isActive ? (
                              <span className="inline-flex items-center text-emerald-600 font-medium">
                                <span className="w-1.5 h-1.5 bg-emerald-500 rounded-full mr-1.5" />
                                Đang dùng
                              </span>
                            ) : (
                              <span className="inline-flex items-center text-slate-400">
                                <span className="w-1.5 h-1.5 bg-slate-300 rounded-full mr-1.5" />
                                Đã tắt
                              </span>
                            )}
                          </td>

                          <td className="p-2.5 text-right space-x-1 whitespace-nowrap">
                            {isEditing ? (
                              <>
                                <Button
                                  variant="primary"
                                  onClick={() => handleSaveEdit(u)}
                                  disabled={loading}
                                >
                                  Lưu
                                </Button>
                                <Button
                                  variant="secondary"
                                  onClick={() => setEditingUnitId(null)}
                                  disabled={loading}
                                >
                                  Hủy
                                </Button>
                              </>
                            ) : (
                              <>
                                {!u.isBase && (
                                  <>
                                    <Button
                                      variant="ghost"
                                      onClick={() => handleStartEdit(u)}
                                      title="Sửa cấu hình quy cách"
                                    >
                                      Sửa
                                    </Button>
                                    <Button
                                      variant="ghost"
                                      onClick={() => handleToggleActive(u)}
                                      title={u.isActive ? "Tắt quy cách" : "Bật quy cách"}
                                      className={u.isActive ? "text-rose-600" : "text-emerald-600"}
                                    >
                                      {u.isActive ? "Tắt" : "Bật"}
                                    </Button>
                                  </>
                                )}
                                <Button
                                  variant="ghost"
                                  onClick={() => setHistoryModalUnit(u)}
                                  title="Xem lịch sử thay đổi hệ số"
                                >
                                  <History size={13} className="mr-0.5 inline" /> Lịch sử
                                </Button>
                              </>
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

          {/* Form thêm quy cách mới */}
          {showAddForm && (
            <div className="p-4 bg-indigo-50/50 border border-indigo-200 rounded-lg space-y-3">
              <div className="flex items-center justify-between">
                <h5 className="font-semibold text-indigo-900 text-xs uppercase tracking-wider flex items-center gap-1.5">
                  <Plus size={14} /> Thêm đơn vị quy đổi mới
                </h5>
                <button
                  type="button"
                  onClick={() => setShowAddForm(false)}
                  className="text-slate-400 hover:text-slate-600"
                >
                  <X size={16} />
                </button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                <div>
                  <label className="block text-xs font-medium text-slate-700 mb-1">
                    Đơn vị quy đổi <span className="text-rose-500">*</span>
                  </label>
                  <Select
                    value={newUnitId}
                    onChange={(e) => {
                      const uid = e.target.value;
                      setNewUnitId(uid);
                      const selected = tenantUnits.find((u) => u.id === uid);
                      if (selected) {
                        setNewDecimalPlaces(selected.decimalPlaces);
                      }
                    }}
                  >
                    {availableUnitsForAdd.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.name} ({u.symbol}) — Nhóm {u.measureGroup}
                      </option>
                    ))}
                  </Select>
                </div>

                <div>
                  <label className="block text-xs font-medium text-slate-700 mb-1">
                    Hệ số quy đổi về đơn vị gốc <span className="text-rose-500">*</span>
                  </label>
                  <Input
                    type="number"
                    min="0.000001"
                    step="any"
                    value={newFactor}
                    onChange={(e) => setNewFactor(Number(e.target.value))}
                    placeholder="VD: 25 cho bao 25kg, 1000 cho tấn"
                  />
                  <span className="text-[10px] text-slate-500 block mt-0.5">
                    1 đơn vị mới = {newFactor || 0} {baseUnitSymbol}
                  </span>
                </div>

                <div>
                  <label className="block text-xs font-medium text-slate-700 mb-1">
                    Số chữ số thập phân
                  </label>
                  <Select
                    value={newDecimalPlaces}
                    onChange={(e) => setNewDecimalPlaces(Number(e.target.value))}
                  >
                    <option value={0}>0 (Bao, hộp, cái nguyên)</option>
                    <option value={1}>1 (0.1)</option>
                    <option value={2}>2 (0.01)</option>
                    <option value={3}>3 (0.001 - G, ML, Khối lượng)</option>
                  </Select>
                </div>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-1">
                <div>
                  <label className="block text-xs font-medium text-slate-700 mb-1">
                    Mã vạch riêng cho quy cách (tùy chọn)
                  </label>
                  <Input
                    value={newBarcode}
                    onChange={(e) => setNewBarcode(e.target.value)}
                    placeholder="Quét hoặc nhập mã vạch bao..."
                  />
                </div>

                <div className="flex items-center gap-4 pt-4">
                  <label className="flex items-center gap-1.5 text-xs text-slate-700">
                    <input
                      type="checkbox"
                      checked={newCanPurchase}
                      onChange={(e) => setNewCanPurchase(e.target.checked)}
                    />{" "}
                    Cho phép Mua
                  </label>
                  <label className="flex items-center gap-1.5 text-xs text-slate-700">
                    <input
                      type="checkbox"
                      checked={newCanSell}
                      onChange={(e) => setNewCanSell(e.target.checked)}
                    />{" "}
                    Cho phép Bán
                  </label>
                  <label className="flex items-center gap-1.5 text-xs text-slate-700">
                    <input
                      type="checkbox"
                      checked={newIsDefaultSale}
                      onChange={(e) => setNewIsDefaultSale(e.target.checked)}
                    />{" "}
                    Mặc định bán
                  </label>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2 border-t border-indigo-100">
                <Button
                  variant="secondary"
                  onClick={() => setShowAddForm(false)}
                  disabled={loading}
                >
                  Hủy
                </Button>
                <Button
                  variant="primary"
                  onClick={handleCreateSkuUnit}
                  disabled={loading}
                >
                  Xác nhận thêm
                </Button>
              </div>
            </div>
          )}

          <div className="flex justify-end pt-3 border-t border-slate-200">
            <Button variant="secondary" onClick={onClose}>
              Đóng
            </Button>
          </div>
        </div>
      </Modal>

      {/* Modal Lịch sử hệ số */}
      <SkuUnitHistoryModal
        isOpen={Boolean(historyModalUnit)}
        onClose={() => setHistoryModalUnit(null)}
        productId={sku.productId}
        variantId={sku.id}
        skuUnit={historyModalUnit}
        baseUnitSymbol={baseUnitSymbol}
      />
    </>
  );
}
