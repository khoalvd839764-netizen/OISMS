import {
  Building2,
  Plus,
  Power,
  RefreshCw,
  Search,
  ShieldCheck,
  Truck,
  UserRoundCog,
  Warehouse,
} from 'lucide-react';
import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Badge, Button, EmptyState, Input, Modal, PageTitle, Select } from '../components/common/ui';
import { TableSkeleton } from '../components/feedback/Skeleton';
import { useToast } from '../components/feedback/Toast';
import { useAuth } from '../context/AuthContext';
import { organizationApi } from '../api/organization.api';
import { parseApiError } from '../api/client';
import type {
  BranchResponse,
  SupplierResponse,
  WarehouseResponse,
} from '../types/organization.types';

type OrgTab = 'branches' | 'warehouses' | 'suppliers' | 'users';

export function OrganizationPage() {
  const { user, role } = useAuth();
  const { showToast } = useToast();

  const [tab, setTab] = useState<OrgTab>('branches');
  const [branches, setBranches] = useState<BranchResponse[]>([]);
  const [warehouses, setWarehouses] = useState<WarehouseResponse[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  const loadOrganizationData = useCallback(async () => {
    setLoading(true);
    try {
      const [brData, whData, supData] = await Promise.all([
        organizationApi.getBranches(),
        organizationApi.getWarehouses(),
        organizationApi.getSuppliers(),
      ]);
      setBranches(brData);
      setWarehouses(whData);
      setSuppliers(supData);
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể tải dữ liệu tổ chức', parsed.message);
    } finally {
      setLoading(false);
    }
  }, [showToast]);

  useEffect(() => {
    void loadOrganizationData();
  }, [loadOrganizationData]);

  // Modal states
  const [branchModalOpen, setBranchModalOpen] = useState(false);
  const [branchName, setBranchName] = useState('');
  const [branchAddress, setBranchAddress] = useState('');
  const [branchPhone, setBranchPhone] = useState('');

  const [warehouseModalOpen, setWarehouseModalOpen] = useState(false);
  const [whName, setWhName] = useState('');
  const [whBranchId, setWhBranchId] = useState('');

  const [supplierModalOpen, setSupplierModalOpen] = useState(false);
  const [supName, setSupName] = useState('');
  const [supCode, setSupCode] = useState('');
  const [supContact, setSupContact] = useState('');
  const [supPhone, setSupPhone] = useState('');
  const [supEmail, setSupEmail] = useState('');
  const [supAddress, setSupAddress] = useState('');

  const handleCreateBranch = async (e: FormEvent) => {
    e.preventDefault();
    if (!branchName.trim()) return;
    setSubmitting(true);
    try {
      await organizationApi.createBranch({
        name: branchName.trim(),
        address: branchAddress.trim() || null,
        phone: branchPhone.trim() || null,
      });
      showToast('success', 'Đã tạo chi nhánh mới', `Chi nhánh "${branchName}" đã được lưu.`);
      setBranchModalOpen(false);
      setBranchName('');
      setBranchAddress('');
      setBranchPhone('');
      await loadOrganizationData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi tạo chi nhánh', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleBranch = async (branch: BranchResponse) => {
    try {
      await organizationApi.toggleBranchStatus(branch.id);
      showToast(
        'info',
        'Đã đổi trạng thái chi nhánh',
        `Chi nhánh "${branch.name}" đã chuyển sang ${branch.isActive ? 'Tạm ngưng' : 'Đang hoạt động'}.`
      );
      await loadOrganizationData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể đổi trạng thái chi nhánh', parsed.message);
    }
  };

  const handleCreateWarehouse = async (e: FormEvent) => {
    e.preventDefault();
    if (!whName.trim() || !whBranchId) return;
    setSubmitting(true);
    try {
      await organizationApi.createWarehouse({
        branchId: whBranchId,
        name: whName.trim(),
      });
      showToast('success', 'Đã tạo kho vật lý mới', `Kho "${whName}" đã được gắn vào chi nhánh.`);
      setWarehouseModalOpen(false);
      setWhName('');
      await loadOrganizationData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi tạo kho', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleWarehouse = async (wh: WarehouseResponse) => {
    try {
      await organizationApi.toggleWarehouseStatus(wh.id);
      showToast(
        'info',
        'Đã đổi trạng thái kho',
        `Kho "${wh.name}" đã chuyển sang ${wh.isActive ? 'Tạm khóa' : 'Hoạt động'}.`
      );
      await loadOrganizationData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể đổi trạng thái kho', parsed.message);
    }
  };

  const handleCreateSupplier = async (e: FormEvent) => {
    e.preventDefault();
    if (!supName.trim()) return;
    setSubmitting(true);
    try {
      await organizationApi.createSupplier({
        name: supName.trim(),
        code: supCode.trim() || null,
        contactName: supContact.trim() || null,
        phone: supPhone.trim() || null,
        email: supEmail.trim() || null,
        address: supAddress.trim() || null,
      });
      showToast('success', 'Đã thêm nhà cung cấp', `Nhà cung cấp "${supName}" đã được lưu.`);
      setSupplierModalOpen(false);
      setSupName('');
      setSupCode('');
      setSupContact('');
      setSupPhone('');
      setSupEmail('');
      setSupAddress('');
      await loadOrganizationData();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi tạo nhà cung cấp', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const q = searchQuery.trim().toLowerCase();

  return (
    <>
      <PageTitle
        eyebrow="FR-ORG · QUẢN TRỊ DOANH NGHIỆP (OWNER ONLY)"
        title="Chi nhánh, Kho vật lý, Nhà cung cấp & Phân quyền"
        description="Quản lý cấu trúc chi nhánh, kho hàng, đối tác cung ứng và kiểm soát phạm vi truy cập trong Tenant."
        actions={
          <>
            <Button variant="secondary" onClick={() => void loadOrganizationData()}>
              <RefreshCw size={16} /> Làm mới
            </Button>
            {tab === 'branches' && (
              <Button onClick={() => setBranchModalOpen(true)}>
                <Plus size={17} /> Thêm chi nhánh
              </Button>
            )}
            {tab === 'warehouses' && (
              <Button
                onClick={() => {
                  setWhBranchId(branches[0]?.id || '');
                  setWarehouseModalOpen(true);
                }}
              >
                <Plus size={17} /> Thêm kho vật lý
              </Button>
            )}
            {tab === 'suppliers' && (
              <Button onClick={() => setSupplierModalOpen(true)}>
                <Plus size={17} /> Thêm nhà cung cấp
              </Button>
            )}
          </>
        }
      />

      <section className="card">
        <div className="tabs">
          <button
            type="button"
            className={tab === 'branches' ? 'active' : ''}
            onClick={() => setTab('branches')}
          >
            <Building2 size={16} /> Chi nhánh ({branches.length})
          </button>
          <button
            type="button"
            className={tab === 'warehouses' ? 'active' : ''}
            onClick={() => setTab('warehouses')}
          >
            <Warehouse size={16} /> Kho vật lý ({warehouses.length})
          </button>
          <button
            type="button"
            className={tab === 'suppliers' ? 'active' : ''}
            onClick={() => setTab('suppliers')}
          >
            <Truck size={16} /> Nhà cung cấp ({suppliers.length})
          </button>
          <button
            type="button"
            className={tab === 'users' ? 'active' : ''}
            onClick={() => setTab('users')}
          >
            <UserRoundCog size={16} /> Tài khoản hiện tại
          </button>
        </div>

        <div className="toolbar">
          <label className="search-field">
            <Search size={18} />
            <Input
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm theo tên chi nhánh, kho hoặc nhà cung cấp..."
            />
          </label>
        </div>

        {loading ? (
          <div style={{ padding: '16px' }}>
            <TableSkeleton rows={4} columns={5} />
          </div>
        ) : tab === 'branches' ? (
          branches.length === 0 ? (
            <EmptyState
              title="Chưa có chi nhánh nào"
              description="Thêm chi nhánh cửa hàng đầu tiên cho doanh nghiệp của bạn."
              action={
                <Button onClick={() => setBranchModalOpen(true)}>
                  <Plus size={16} /> Thêm chi nhánh
                </Button>
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Tên chi nhánh</th>
                    <th>Địa chỉ</th>
                    <th>Điện thoại</th>
                    <th>Số kho trực thuộc</th>
                    <th>Trạng thái</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {branches
                    .filter((b) => !q || b.name.toLowerCase().includes(q))
                    .map((b) => (
                      <tr key={b.id}>
                        <td>
                          <strong>{b.name}</strong>
                        </td>
                        <td>{b.address || '—'}</td>
                        <td>{b.phone || '—'}</td>
                        <td>{b.warehouses?.length || 0} kho</td>
                        <td>
                          <Badge tone={b.isActive ? 'success' : 'neutral'}>
                            {b.isActive ? 'Đang hoạt động' : 'Đã tạm ngưng'}
                          </Badge>
                        </td>
                        <td>
                          <Button
                            variant="ghost"
                            onClick={() => void handleToggleBranch(b)}
                            title="Bật/Tắt trạng thái chi nhánh"
                          >
                            <Power size={16} /> {b.isActive ? 'Tạm ngưng' : 'Kích hoạt'}
                          </Button>
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )
        ) : tab === 'warehouses' ? (
          warehouses.length === 0 ? (
            <EmptyState
              title="Chưa có kho vật lý nào"
              description="Tạo kho vật lý gắn với chi nhánh để thực hiện nhập kho và quản lý tồn."
              action={
                <Button
                  onClick={() => {
                    setWhBranchId(branches[0]?.id || '');
                    setWarehouseModalOpen(true);
                  }}
                >
                  <Plus size={16} /> Tạo kho mới
                </Button>
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Tên kho vật lý</th>
                    <th>Chi nhánh trực thuộc</th>
                    <th>Trạng thái</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {warehouses
                    .filter((w) => !q || w.name.toLowerCase().includes(q) || w.branchName.toLowerCase().includes(q))
                    .map((w) => (
                      <tr key={w.id}>
                        <td>
                          <strong>{w.name}</strong>
                        </td>
                        <td>{w.branchName}</td>
                        <td>
                          <Badge tone={w.isActive ? 'success' : 'neutral'}>
                            {w.isActive ? 'Đang hoạt động' : 'Đã khóa'}
                          </Badge>
                        </td>
                        <td>
                          <Button
                            variant="ghost"
                            onClick={() => void handleToggleWarehouse(w)}
                            title="Bật/Tắt trạng thái kho"
                          >
                            <Power size={16} /> {w.isActive ? 'Khóa kho' : 'Mở khóa'}
                          </Button>
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )
        ) : tab === 'suppliers' ? (
          suppliers.length === 0 ? (
            <EmptyState
              title="Chưa có nhà cung cấp nào"
              description="Thêm nhà cung cấp để lập phiếu nhập kho nhiều dòng."
              action={
                <Button onClick={() => setSupplierModalOpen(true)}>
                  <Plus size={16} /> Thêm nhà cung cấp
                </Button>
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Mã NCC</th>
                    <th>Tên nhà cung cấp</th>
                    <th>Người liên hệ</th>
                    <th>Số điện thoại / Email</th>
                    <th>Địa chỉ</th>
                    <th>Trạng thái</th>
                  </tr>
                </thead>
                <tbody>
                  {suppliers
                    .filter((s) => !q || s.name.toLowerCase().includes(q) || s.code.toLowerCase().includes(q))
                    .map((s) => (
                      <tr key={s.id}>
                        <td>
                          <span className="mono">{s.code}</span>
                        </td>
                        <td>
                          <strong>{s.name}</strong>
                        </td>
                        <td>{s.contactName || '—'}</td>
                        <td>
                          {[s.phone, s.email].filter(Boolean).join(' · ') || '—'}
                        </td>
                        <td>{s.address || '—'}</td>
                        <td>
                          <Badge tone={s.isActive ? 'success' : 'neutral'}>
                            {s.isActive ? 'Đang hợp tác' : 'Ngừng'}
                          </Badge>
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Người dùng phiên hiện tại</th>
                  <th>Mã Tenant</th>
                  <th>Vai trò (RBAC)</th>
                  <th>Trạng thái xác thực</th>
                </tr>
              </thead>
              <tbody>
                {user && (
                  <tr>
                    <td>
                      <div className="person-cell">
                        <div className="avatar">
                          {user.fullName
                            .split(' ')
                            .slice(-2)
                            .map((p) => p[0])
                            .join('')
                            .toUpperCase()}
                        </div>
                        <div>
                          <strong>{user.fullName}</strong>
                          <span>{user.email}</span>
                        </div>
                      </div>
                    </td>
                    <td>
                      <span className="mono">{user.tenantCode}</span> ({user.tenantName})
                    </td>
                    <td>
                      <Badge tone="info">{role}</Badge>
                    </td>
                    <td>
                      <Badge tone="success">JWT Active</Badge>
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <div className="permission-banner">
        <ShieldCheck size={19} />
        <div>
          <strong>Bảo mật đa lớp (Multi-Tenant & Role-Based Access Control)</strong>
          <p>
            Mọi truy vấn đều được gắn JWT Bearer Token chứa <code>tenant_id</code> và <code>role</code>. Backend tự động cô lập dữ liệu qua Global Query Filter và kiểm tra quyền tại từng API endpoint.
          </p>
        </div>
      </div>

      {/* Create Branch Modal */}
      {branchModalOpen && (
        <Modal title="Thêm Chi nhánh mới" onClose={() => setBranchModalOpen(false)}>
          <form onSubmit={handleCreateBranch}>
            <div className="form-grid">
              <label style={{ gridColumn: '1 / -1' }}>
                <span>Tên chi nhánh *</span>
                <Input
                  placeholder="Ví dụ: Chi nhánh Quận 1"
                  value={branchName}
                  onChange={(e) => setBranchName(e.target.value)}
                  required
                />
              </label>
              <label>
                <span>Số điện thoại</span>
                <Input
                  placeholder="0901234567"
                  value={branchPhone}
                  onChange={(e) => setBranchPhone(e.target.value)}
                />
              </label>
              <label>
                <span>Địa chỉ</span>
                <Input
                  placeholder="72 Nguyễn Huệ, Quận 1, TP.HCM"
                  value={branchAddress}
                  onChange={(e) => setBranchAddress(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setBranchModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Tạo chi nhánh'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* Create Warehouse Modal */}
      {warehouseModalOpen && (
        <Modal title="Thêm Kho vật lý mới" onClose={() => setWarehouseModalOpen(false)}>
          <form onSubmit={handleCreateWarehouse}>
            <div className="form-grid">
              <label>
                <span>Chi nhánh trực thuộc *</span>
                <Select
                  value={whBranchId}
                  onChange={(e) => setWhBranchId(e.target.value)}
                  required
                >
                  <option value="">-- Chọn chi nhánh --</option>
                  {branches.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </Select>
              </label>
              <label>
                <span>Tên kho vật lý *</span>
                <Input
                  placeholder="Ví dụ: Kho trung tâm Quận 1"
                  value={whName}
                  onChange={(e) => setWhName(e.target.value)}
                  required
                />
              </label>
            </div>
            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setWarehouseModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Tạo kho vật lý'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* Create Supplier Modal */}
      {supplierModalOpen && (
        <Modal title="Thêm Nhà cung cấp mới" onClose={() => setSupplierModalOpen(false)}>
          <form onSubmit={handleCreateSupplier}>
            <div className="form-grid">
              <label>
                <span>Tên nhà cung cấp *</span>
                <Input
                  placeholder="Công ty TNHH May mặc An Phú"
                  value={supName}
                  onChange={(e) => setSupName(e.target.value)}
                  required
                />
              </label>
              <label>
                <span>Mã nhà cung cấp</span>
                <Input
                  placeholder="Tự sinh nếu để trống"
                  value={supCode}
                  onChange={(e) => setSupCode(e.target.value.toUpperCase())}
                />
              </label>
              <label>
                <span>Người liên hệ</span>
                <Input
                  value={supContact}
                  onChange={(e) => setSupContact(e.target.value)}
                />
              </label>
              <label>
                <span>Số điện thoại</span>
                <Input
                  value={supPhone}
                  onChange={(e) => setSupPhone(e.target.value)}
                />
              </label>
              <label>
                <span>Email</span>
                <Input
                  type="email"
                  value={supEmail}
                  onChange={(e) => setSupEmail(e.target.value)}
                />
              </label>
              <label>
                <span>Địa chỉ</span>
                <Input
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
    </>
  );
}
