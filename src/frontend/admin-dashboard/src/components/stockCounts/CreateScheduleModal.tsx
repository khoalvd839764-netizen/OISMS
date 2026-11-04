import React, { useState } from 'react';
import { stockCountService } from '../../services/stockCountService';
import { X, Calendar, Clock, AlertCircle } from 'lucide-react';

interface CreateScheduleModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
  branches: Array<{ id: string; name: string }>;
  warehouses: Array<{ id: string; name: string; branchId?: string }>;
}

export const CreateScheduleModal: React.FC<CreateScheduleModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
  branches,
  warehouses,
}) => {
  const [branchId, setBranchId] = useState(branches[0]?.id || '');
  const [warehouseId, setWarehouseId] = useState('');
  const [periodType, setPeriodType] = useState<'MidMonth' | 'EndOfMonth'>('MidMonth');
  const [dayOfMonth, setDayOfMonth] = useState<number>(15);
  const [localTime, setLocalTime] = useState<string>('18:00:00');
  const [dueDays, setDueDays] = useState<number>(3);
  const [assigneeUserId] = useState<string>('');
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  React.useEffect(() => {
    if (branches.length > 0 && !branchId) {
      setBranchId(branches[0].id);
    }
  }, [branches, branchId]);

  React.useEffect(() => {
    const whs = warehouses.filter((w) => !branchId || !w.branchId || w.branchId === branchId);
    if (whs.length > 0 && (!warehouseId || !whs.find((w) => w.id === warehouseId))) {
      setWarehouseId(whs[0].id);
    }
  }, [branchId, warehouses, warehouseId]);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!branchId || !warehouseId) {
      setError('Vui lòng chọn chi nhánh và kho kiểm kê.');
      return;
    }

    setLoading(true);
    try {
      // Default to current user if assignee is empty
      const userId = localStorage.getItem('oism_user_id') || '11111111-0000-0000-0000-000000000001';

      await stockCountService.createSchedule({
        branchId,
        warehouseId,
        periodType,
        dayOfMonth: periodType === 'MidMonth' ? dayOfMonth : undefined,
        localTime,
        dueDays,
        assigneeUserId: assigneeUserId || userId,
        isActive: true,
      });

      onSuccess();
      onClose();
    } catch (err: any) {
      const msg = err.response?.data?.detail || err.message || 'Lỗi tạo lịch kiểm kê.';
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  const filteredWarehouses = warehouses.filter(
    (w) => !branchId || !w.branchId || w.branchId === branchId
  );

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="bg-white rounded-2xl shadow-2xl max-w-lg w-full overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        <div className="px-6 py-4 bg-slate-900 text-white flex justify-between items-center">
          <div className="flex items-center gap-2">
            <Calendar className="w-5 h-5 text-indigo-400" />
            <h3 className="font-bold text-base">Thêm Lịch Kiểm Kê Định Kỳ (U03)</h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="text-slate-400 hover:text-white transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4 text-xs">
          {error && (
            <div className="p-3 bg-rose-50 border border-rose-200 rounded-xl text-rose-800 flex items-start gap-2">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <span>{error}</span>
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block font-bold text-slate-700 uppercase mb-1">
                Chi nhánh <span className="text-rose-500">*</span>
              </label>
              <select
                value={branchId}
                onChange={(e) => setBranchId(e.target.value)}
                className="w-full border border-slate-300 rounded-lg p-2 bg-white text-xs"
                required
              >
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block font-bold text-slate-700 uppercase mb-1">
                Kho kiểm kê <span className="text-rose-500">*</span>
              </label>
              <select
                value={warehouseId}
                onChange={(e) => setWarehouseId(e.target.value)}
                className="w-full border border-slate-300 rounded-lg p-2 bg-white text-xs"
                required
              >
                {filteredWarehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label className="block font-bold text-slate-700 uppercase mb-1">
              Loại kỳ kiểm kê <span className="text-rose-500">*</span>
            </label>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => setPeriodType('MidMonth')}
                className={`p-2.5 rounded-xl border text-center font-semibold transition-colors ${
                  periodType === 'MidMonth'
                    ? 'border-indigo-600 bg-indigo-50 text-indigo-700'
                    : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                }`}
              >
                Giữa tháng (MidMonth)
              </button>
              <button
                type="button"
                onClick={() => setPeriodType('EndOfMonth')}
                className={`p-2.5 rounded-xl border text-center font-semibold transition-colors ${
                  periodType === 'EndOfMonth'
                    ? 'border-indigo-600 bg-indigo-50 text-indigo-700'
                    : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                }`}
              >
                Cuối tháng (EndOfMonth)
              </button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            {periodType === 'MidMonth' ? (
              <div>
                <label className="block font-bold text-slate-700 uppercase mb-1">
                  Ngày trong tháng (1 - 28)
                </label>
                <input
                  type="number"
                  min="1"
                  max="28"
                  value={dayOfMonth}
                  onChange={(e) => setDayOfMonth(Number(e.target.value))}
                  className="w-full border border-slate-300 rounded-lg p-2 text-xs"
                  required
                />
              </div>
            ) : (
              <div>
                <label className="block font-bold text-slate-700 uppercase mb-1">
                  Mốc ngày
                </label>
                <input
                  type="text"
                  disabled
                  value="Ngày cuối cùng của tháng"
                  className="w-full border border-slate-200 bg-slate-100 rounded-lg p-2 text-xs text-slate-500"
                />
              </div>
            )}

            <div>
              <label className="block font-bold text-slate-700 uppercase mb-1">
                Giờ kiểm kê (UTC+7)
              </label>
              <div className="relative">
                <input
                  type="time"
                  step="1"
                  value={localTime}
                  onChange={(e) => setLocalTime(e.target.value)}
                  className="w-full border border-slate-300 rounded-lg p-2 text-xs"
                  required
                />
                <Clock className="w-3.5 h-3.5 text-slate-400 absolute right-2.5 top-2.5 pointer-events-none" />
              </div>
            </div>
          </div>

          <div>
            <label className="block font-bold text-slate-700 uppercase mb-1">
              Thời hạn hoàn thành (ngày)
            </label>
            <input
              type="number"
              min="1"
              max="14"
              value={dueDays}
              onChange={(e) => setDueDays(Number(e.target.value))}
              className="w-full border border-slate-300 rounded-lg p-2 text-xs"
              required
            />
          </div>

          <div className="pt-3 border-t border-slate-200 flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              disabled={loading}
              className="px-4 py-2 border border-slate-300 rounded-xl text-slate-700 font-semibold hover:bg-slate-50"
            >
              Hủy
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white font-semibold rounded-xl shadow-xs"
            >
              {loading ? 'Đang lưu...' : 'Tạo lịch kiểm kê'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
