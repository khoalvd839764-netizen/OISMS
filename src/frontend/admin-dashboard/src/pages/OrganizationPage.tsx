import { Building2, Plus, Search, ShieldCheck, UserRoundCog, AlertCircle } from "lucide-react";
import { useEffect, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";

export function OrganizationPage() {
  const { user } = useAuth();
  const [tab, setTab] = useState<"people" | "branches">("branches");
  const [open, setOpen] = useState(false);
  const [warehouseModalBranchId, setWarehouseModalBranchId] = useState<string | null>(null);
  const [newWarehouseName, setNewWarehouseName] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");

  // Danh sách chi nhánh & kho hàng thật
  const [branches, setBranches] = useState<Array<{ id: string; name: string; address?: string; phone?: string; isActive?: boolean }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; code?: string; address?: string; branchId: string; isActive?: boolean }>>([]);
  const [users, setUsers] = useState<Array<{ id: string; tenantId: string; email: string; displayName: string; phone?: string; role: string; isActive: boolean; createdAt: string }>>([]);

  // Form Thêm chi nhánh mới
  const [branchName, setBranchName] = useState("");
  const [branchAddress, setBranchAddress] = useState("");
  const [branchPhone, setBranchPhone] = useState("");

  // Form Thêm nhân viên mới (CS-02)
  const [userDisplayName, setUserDisplayName] = useState("");
  const [userEmail, setUserEmail] = useState("");
  const [userPhone, setUserPhone] = useState("");
  const [userPassword, setUserPassword] = useState("");
  const [userRole, setUserRole] = useState("Staff");

  // Modal Sửa nhân viên (CS-02)
  const [editingUser, setEditingUser] = useState<{ id: string; email: string; displayName: string; phone?: string; role: string; isActive: boolean } | null>(null);
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editPhone, setEditPhone] = useState("");
  const [editRole, setEditRole] = useState("Staff");
  const [editIsActive, setEditIsActive] = useState(true);

  const loadOrgData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [brRes, whRes, usRes] = await Promise.all([
        apiAdapter.getBranches(),
        apiAdapter.getWarehouses(),
        apiAdapter.getUsers(),
      ]);
      if (brRes.error) {
        setError(brRes.error.message || "Không thể tải danh sách chi nhánh.");
      } else if (whRes.error) {
        setError(whRes.error.message || "Không thể tải danh sách kho hàng.");
      } else {
        if ("data" in brRes && Array.isArray(brRes.data)) {
          setBranches(brRes.data);
        }
        if ("data" in whRes && Array.isArray(whRes.data)) {
          setWarehouses(whRes.data);
        }
        if ("data" in usRes && Array.isArray(usRes.data)) {
          setUsers(usRes.data);
        }
      }
    } catch (err: any) {
      setError(err?.message || "Lỗi nạp dữ liệu chi nhánh và kho hàng.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadOrgData();
  }, []);

  const handleCreateBranch = async () => {
    setError(null);
    if (!branchName.trim()) {
      setError("Vui lòng nhập tên chi nhánh.");
      return;
    }

    setLoading(true);
    const res = await apiAdapter.createBranch({
      name: branchName.trim(),
      address: branchAddress.trim() || undefined,
      phone: branchPhone.trim() || undefined,
    });
    setLoading(false);

    if (res.error) {
      setError(res.error.message || "Tạo chi nhánh thất bại.");
    } else {
      setOpen(false);
      setBranchName("");
      setBranchAddress("");
      setBranchPhone("");
      loadOrgData();
    }
  };

  const handleToggleBranch = async (branchId: string) => {
    setLoading(true);
    setError(null);
    const res = await apiAdapter.toggleBranchStatus(branchId);
    if (res && res.error) {
      setError(res.error.message || "Không thể thay đổi trạng thái chi nhánh.");
    } else {
      await loadOrgData();
    }
    setLoading(false);
  };

  const handleCreateWarehouse = async () => {
    if (!warehouseModalBranchId || !newWarehouseName.trim()) {
      setError("Vui lòng nhập tên kho hàng.");
      return;
    }
    setError(null);
    setLoading(true);
    const res = await apiAdapter.createWarehouse({
      branchId: warehouseModalBranchId,
      name: newWarehouseName.trim(),
    });
    setLoading(false);
    if (res.error) {
      setError(res.error.message);
    } else {
      setWarehouseModalBranchId(null);
      setNewWarehouseName("");
      loadOrgData();
    }
  };

  const handleToggleWarehouse = async (warehouseId: string) => {
    setLoading(true);
    setError(null);
    const res = await apiAdapter.toggleWarehouseStatus(warehouseId);
    if (res && res.error) {
      setError(res.error.message || "Không thể thay đổi trạng thái kho hàng.");
    } else {
      await loadOrgData();
    }
    setLoading(false);
  };

  const handleCreateUser = async () => {
    setError(null);
    if (!userDisplayName.trim()) {
      setError("Vui lòng nhập họ và tên nhân viên.");
      return;
    }
    if (!userEmail.trim()) {
      setError("Vui lòng nhập email đăng nhập.");
      return;
    }
    if (!userPassword || userPassword.length < 6) {
      setError("Mật khẩu phải chứa ít nhất 6 ký tự.");
      return;
    }

    setLoading(true);
    const res = await apiAdapter.createUser({
      displayName: userDisplayName.trim(),
      email: userEmail.trim(),
      phone: userPhone.trim() || undefined,
      password: userPassword,
      role: userRole,
    });
    setLoading(false);

    if (res.error) {
      setError(res.error.message || "Tạo nhân viên thất bại.");
    } else {
      setOpen(false);
      setUserDisplayName("");
      setUserEmail("");
      setUserPhone("");
      setUserPassword("");
      setUserRole("Staff");
      loadOrgData();
    }
  };

  const openEditUserModal = (u: { id: string; email: string; displayName: string; phone?: string; role: string; isActive: boolean }) => {
    setEditingUser(u);
    setEditDisplayName(u.displayName);
    setEditPhone(u.phone || "");
    setEditRole(u.role);
    setEditIsActive(u.isActive);
    setError(null);
  };

  const handleUpdateUser = async () => {
    if (!editingUser) return;
    setError(null);
    if (!editDisplayName.trim()) {
      setError("Vui lòng nhập họ và tên nhân viên.");
      return;
    }

    setLoading(true);
    const res = await apiAdapter.updateUser(editingUser.id, {
      displayName: editDisplayName.trim(),
      phone: editPhone.trim() || undefined,
      role: editRole,
      isActive: editIsActive,
    });
    setLoading(false);

    if (res.error) {
      setError(res.error.message || "Cập nhật nhân viên thất bại.");
    } else {
      setEditingUser(null);
      await loadOrgData();
    }
  };

  const handleToggleUserStatus = async (userId: string, currentActive: boolean) => {
    setError(null);
    setLoading(true);
    const res = await apiAdapter.updateUserStatus(userId, !currentActive);
    setLoading(false);
    if (res.error) {
      setError(res.error.message || "Không thể cập nhật trạng thái nhân viên.");
    } else {
      loadOrgData();
    }
  };

  const filteredBranches = branches.filter((b) => {
    const matchesSearch = `${b.name} ${b.address || ""} ${b.phone || ""}`.toLowerCase().includes(searchQuery.toLowerCase());
    const active = b.isActive !== false;
    const matchesStatus =
      statusFilter === "all" ||
      (statusFilter === "active" && active) ||
      (statusFilter === "inactive" && !active);
    return matchesSearch && matchesStatus;
  });

  const filteredUsers = users.filter((u) => {
    const matchesSearch = `${u.displayName} ${u.email} ${u.phone || ""}`.toLowerCase().includes(searchQuery.toLowerCase());
    const active = u.isActive !== false;
    const matchesStatus =
      statusFilter === "all" ||
      (statusFilter === "active" && active) ||
      (statusFilter === "inactive" && !active);
    return matchesSearch && matchesStatus;
  });

  return (
    <>
      <PageTitle
        eyebrow="FR-AUTH · PHÂN QUYỀN RBAC"
        title="Nhân viên & chi nhánh"
        description="Quản lý chi nhánh, kho hàng trực thuộc và danh sách nhân sự phân quyền theo vai trò (Owner, Staff, Cashier)."
        actions={
          user?.role === "Owner" ? (
            <Button onClick={() => { setError(null); setOpen(true); }}>
              <Plus size={17} />
              {tab === "people" ? "Thêm nhân viên" : "Thêm chi nhánh"}
            </Button>
          ) : undefined
        }
      />

      <section className="card">
        <div className="tabs">
          <button className={tab === "branches" ? "active" : ""} onClick={() => setTab("branches")}>
            <Building2 size={16} />Chi nhánh & kho ({branches.length} chi nhánh · {warehouses.length} kho)
          </button>
          <button className={tab === "people" ? "active" : ""} onClick={() => setTab("people")}>
            <UserRoundCog size={16} />Nhân viên ({users.length} người)
          </button>
        </div>

        <div className="toolbar">
          <label className="search-field">
            <Search size={18} />
            <Input
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={tab === "people" ? "Tìm tên, email hoặc số điện thoại..." : "Tìm chi nhánh hoặc địa chỉ..."}
            />
          </label>
          <Select aria-label="Bộ lọc trạng thái" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="all">Tất cả trạng thái</option>
            <option value="active">Đang hoạt động</option>
            <option value="inactive">Đã tạm dừng</option>
          </Select>
        </div>

        {error && (
          <div style={{ margin: "16px", padding: "14px 16px", background: "#fef2f2", border: "1px solid #fecaca", borderRadius: "10px", color: "#b91c1c", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={18} />
              <span>{error}</span>
            </div>
            <button
              type="button"
              onClick={loadOrgData}
              style={{ background: "#fff", border: "1px solid #f87171", borderRadius: "6px", padding: "4px 10px", color: "#b91c1c", fontSize: "12px", cursor: "pointer", fontWeight: 600 }}
            >
              Thử lại
            </button>
          </div>
        )}

        {tab === "people" ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Nhân viên</th>
                  <th>Vai trò</th>
                  <th>Cửa hàng / Doanh nghiệp</th>
                  <th>Trạng thái</th>
                  <th style={{ textAlign: "right" }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {filteredUsers.length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                      {loading ? "Đang tải danh sách nhân viên..." : "Chưa có nhân viên nào phù hợp bộ lọc."}
                    </td>
                  </tr>
                ) : (
                  filteredUsers.map((u) => {
                    const isSelf = user?.email === u.email;
                    const initials = u.displayName.slice(0, 2).toUpperCase();
                    return (
                      <tr key={u.id}>
                        <td>
                          <div className="person-cell">
                            <div className="avatar" style={{ background: u.role === "Owner" ? "#2563eb" : "#475569" }}>
                              {initials}
                            </div>
                            <div>
                              <strong>{u.displayName} {isSelf && <span style={{ fontSize: "11px", color: "#2563eb" }}>(Bạn)</span>}</strong>
                              <span>{u.email} {u.phone ? `· ${u.phone}` : ""}</span>
                            </div>
                          </div>
                        </td>
                        <td>
                          <Badge tone={u.role === "Owner" ? "info" : u.role === "Cashier" ? "warning" : "neutral"}>
                            {u.role}
                          </Badge>
                        </td>
                        <td>{user?.tenantName || "Cửa hàng chính"}</td>
                        <td>
                          <Badge tone={u.isActive ? "success" : "neutral"}>
                            {u.isActive ? "Đang hoạt động" : "Đã tạm dừng"}
                          </Badge>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <div style={{ display: "flex", gap: "6px", justifyContent: "flex-end", alignItems: "center" }}>
                            {user?.role === "Owner" && (
                              <button
                                type="button"
                                onClick={() => openEditUserModal(u)}
                                disabled={loading}
                                style={{
                                  background: "#f1f5f9",
                                  border: "1px solid #cbd5e1",
                                  color: "#334155",
                                  borderRadius: "6px",
                                  padding: "4px 8px",
                                  fontSize: "11px",
                                  fontWeight: 600,
                                  cursor: "pointer",
                                }}
                              >
                                Sửa
                              </button>
                            )}
                            {user?.role === "Owner" && !isSelf && u.role !== "Owner" && (
                              <button
                                type="button"
                                onClick={() => handleToggleUserStatus(u.id, u.isActive)}
                                disabled={loading}
                                style={{
                                  background: u.isActive ? "#fee2e2" : "#f0fdf4",
                                  border: `1px solid ${u.isActive ? "#f87171" : "#86efac"}`,
                                  color: u.isActive ? "#b91c1c" : "#15803d",
                                  borderRadius: "6px",
                                  padding: "4px 8px",
                                  fontSize: "11px",
                                  fontWeight: 600,
                                  cursor: "pointer",
                                }}
                              >
                                {u.isActive ? "Tạm dừng" : "Kích hoạt"}
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="branch-grid" style={{ padding: "16px", display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: "14px" }}>
            {loading && branches.length === 0 ? (
              <div style={{ gridColumn: "1 / -1", textAlign: "center", padding: "32px 0", color: "#64748b" }}>
                Đang tải danh sách chi nhánh và kho hàng...
              </div>
            ) : error && branches.length === 0 ? null : filteredBranches.length === 0 ? (
              <div style={{ gridColumn: "1 / -1", textAlign: "center", padding: "32px 0", color: "#64748b" }}>
                Chưa có chi nhánh nào khớp bộ lọc. Bấm <strong>"Thêm chi nhánh"</strong> để thiết lập địa điểm bán hàng.
              </div>
            ) : (
              filteredBranches.map((b) => {
                const whList = warehouses.filter((w) => w.branchId === b.id);
                return (
                  <article key={b.id} style={{ border: "1px solid #e2e8f0", borderRadius: "10px", padding: "14px", background: "#fff", display: "flex", flexDirection: "column", justifyContent: "space-between" }}>
                    <div>
                      <div style={{ display: "flex", justifyContent: "space-between", gap: "10px", alignItems: "flex-start" }}>
                        <div style={{ display: "flex", gap: "10px", alignItems: "flex-start" }}>
                          <span style={{ background: "#eff6ff", color: "#1d4ed8", padding: "8px", borderRadius: "8px" }}>
                            <Building2 size={20} />
                          </span>
                          <div>
                            <strong>{b.name}</strong>
                            <p style={{ margin: "4px 0", color: "#64748b", fontSize: "12px" }}>{b.address || "Chưa cập nhật địa chỉ"}</p>
                            <Badge tone={b.isActive !== false ? "success" : "neutral"}>
                              {b.isActive !== false ? "Đang hoạt động" : "Đã tạm dừng"}
                            </Badge>
                          </div>
                        </div>
                        {user?.role === "Owner" && (
                          <button
                            type="button"
                            onClick={() => handleToggleBranch(b.id)}
                            disabled={loading}
                            style={{ background: "#f8fafc", border: "1px solid #cbd5e1", borderRadius: "6px", padding: "4px 8px", fontSize: "11px", cursor: "pointer" }}
                          >
                            {b.isActive !== false ? "Tạm dừng" : "Kích hoạt"}
                          </button>
                        )}
                      </div>

                      <div style={{ marginTop: "12px", borderTop: "1px dashed #cbd5e1", paddingTop: "8px" }}>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                          <small style={{ color: "#64748b", fontWeight: 600 }}>Kho hàng trực thuộc ({whList.length}):</small>
                          <button
                            type="button"
                            onClick={() => { setError(null); setNewWarehouseName(""); setWarehouseModalBranchId(b.id); }}
                            style={{ background: "none", border: "none", color: "#1769e0", fontSize: "11px", fontWeight: 600, cursor: "pointer" }}
                          >
                            + Thêm kho
                          </button>
                        </div>
                        {whList.length === 0 ? (
                          <p style={{ fontSize: "12px", color: "#94a3b8", margin: "6px 0 0" }}>Chưa có kho lưu trữ nào thuộc chi nhánh này.</p>
                        ) : (
                          <ul style={{ margin: "6px 0 0", padding: 0, listStyle: "none", display: "flex", flexDirection: "column", gap: "4px" }}>
                            {whList.map((w) => (
                              <li key={w.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", fontSize: "12px", color: "#334155", background: "#f8fafc", padding: "4px 8px", borderRadius: "6px" }}>
                                <span>
                                  {w.name} {w.isActive === false ? <em style={{ color: "#94a3b8" }}>(Tạm ngưng)</em> : null}
                                </span>
                                <button
                                  type="button"
                                  onClick={() => handleToggleWarehouse(w.id)}
                                  style={{ background: "none", border: "none", color: "#64748b", fontSize: "11px", cursor: "pointer", textDecoration: "underline" }}
                                >
                                  {w.isActive === false ? "Bật" : "Tắt"}
                                </button>
                              </li>
                            ))}
                          </ul>
                        )}
                      </div>
                    </div>
                  </article>
                );
              })
            )}
          </div>
        )}
      </section>

      <div className="permission-banner">
        <ShieldCheck size={19} />
        <div>
          <strong>Bảo mật & Phân quyền đa khách thuê (Multi-Tenant RBAC)</strong>
          <p>
            Thông tin nhân viên và chi nhánh được phân lập tuyệt đối theo <code>TenantId</code> từ Token. Giao diện chỉ hiển thị chức năng cho phép của vai trò <code>{user?.role || "Staff"}</code>.
          </p>
        </div>
      </div>

      {open && (
        <Modal title={tab === "people" ? "Thêm nhân viên mới" : "Thêm chi nhánh mới"} onClose={() => setOpen(false)}>
          {error && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px", display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}

          <div className="form-grid">
            {tab === "people" ? (
              <>
                <label>
                  <span>Họ và tên nhân viên *</span>
                  <Input
                    placeholder="Ví dụ: Trần Quốc Tuấn"
                    value={userDisplayName}
                    onChange={(e) => setUserDisplayName(e.target.value)}
                    required
                  />
                </label>
                <label>
                  <span>Email đăng nhập *</span>
                  <Input
                    type="email"
                    placeholder="staff@cuahang.vn"
                    value={userEmail}
                    onChange={(e) => setUserEmail(e.target.value)}
                    required
                  />
                </label>
                <label>
                  <span>Số điện thoại</span>
                  <Input
                    placeholder="0912345678"
                    value={userPhone}
                    onChange={(e) => setUserPhone(e.target.value)}
                  />
                </label>
                <label>
                  <span>Mật khẩu khởi tạo * (tối thiểu 6 ký tự)</span>
                  <Input
                    type="password"
                    placeholder="Mật khẩu ít nhất 6 ký tự..."
                    value={userPassword}
                    onChange={(e) => setUserPassword(e.target.value)}
                    required
                  />
                </label>
                <label>
                  <span>Vai trò phân quyền</span>
                  <Select value={userRole} onChange={(e) => setUserRole(e.target.value)}>
                    <option value="Staff">Nhân viên kho & Bán hàng (Staff)</option>
                    <option value="Cashier">Thu ngân (Cashier - chỉ truy cập POS)</option>
                  </Select>
                </label>
              </>
            ) : (
              <>
                <label>
                  <span>Tên chi nhánh *</span>
                  <Input
                    placeholder="Ví dụ: Chi nhánh Bình Thạnh"
                    value={branchName}
                    onChange={(e) => setBranchName(e.target.value)}
                    required
                  />
                </label>
                <label>
                  <span>Số điện thoại</span>
                  <Input
                    placeholder="028 1234 5678"
                    value={branchPhone}
                    onChange={(e) => setBranchPhone(e.target.value)}
                  />
                </label>
                <label className="full-field">
                  <span>Địa chỉ</span>
                  <Input
                    placeholder="Số nhà, tên đường, phường/xã, quận/huyện..."
                    value={branchAddress}
                    onChange={(e) => setBranchAddress(e.target.value)}
                  />
                </label>
              </>
            )}
          </div>

          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setOpen(false)}>Hủy</Button>
            <Button
              onClick={tab === "branches" ? handleCreateBranch : handleCreateUser}
              disabled={loading}
            >
              {loading ? "Đang lưu..." : "Lưu thông tin"}
            </Button>
          </div>
        </Modal>
      )}

      {warehouseModalBranchId && (
        <Modal title="Thêm Kho hàng mới trực thuộc Chi nhánh" onClose={() => setWarehouseModalBranchId(null)}>
          {error && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px" }}>
              {error}
            </div>
          )}
          <div className="form-grid">
            <label className="full-field">
              <span>Tên kho hàng *</span>
              <Input
                placeholder="Ví dụ: Kho Tổng Quận 1 / Kho Hàng Lạnh..."
                value={newWarehouseName}
                onChange={(e) => setNewWarehouseName(e.target.value)}
                required
              />
            </label>
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setWarehouseModalBranchId(null)}>Hủy</Button>
            <Button onClick={handleCreateWarehouse} disabled={loading}>
              {loading ? "Đang tạo..." : "Tạo kho hàng"}
            </Button>
          </div>
        </Modal>
      )}
      {editingUser && (
        <Modal title={`Cập nhật thông tin nhân viên: ${editingUser.displayName}`} onClose={() => setEditingUser(null)}>
          {error && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px", display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}
          <div className="form-grid">
            <label>
              <span>Email tài khoản (Bất biến)</span>
              <Input value={editingUser.email} disabled />
            </label>
            <label>
              <span>Họ và tên nhân viên *</span>
              <Input
                value={editDisplayName}
                onChange={(e) => setEditDisplayName(e.target.value)}
                required
              />
            </label>
            <label>
              <span>Số điện thoại</span>
              <Input
                value={editPhone}
                onChange={(e) => setEditPhone(e.target.value)}
                placeholder="0912345678"
              />
            </label>
            <label>
              <span>Vai trò phân quyền</span>
              <Select
                value={editRole}
                onChange={(e) => setEditRole(e.target.value)}
                disabled={user?.email === editingUser.email} // Chặn tự hạ quyền của chính mình
              >
                <option value="Staff">Nhân viên kho & Bán hàng (Staff)</option>
                <option value="Cashier">Thu ngân (Cashier - chỉ truy cập POS)</option>
                <option value="Owner">Chủ cửa hàng (Owner)</option>
              </Select>
            </label>
            <label>
              <span>Trạng thái hoạt động</span>
              <Select
                value={editIsActive ? "true" : "false"}
                onChange={(e) => setEditIsActive(e.target.value === "true")}
                disabled={user?.email === editingUser.email} // Chặn tự khóa tài khoản của chính mình
              >
                <option value="true">Đang hoạt động</option>
                <option value="false">Tạm dừng tài khoản</option>
              </Select>
            </label>
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setEditingUser(null)}>Hủy</Button>
            <Button onClick={handleUpdateUser} disabled={loading}>
              {loading ? "Đang lưu..." : "Lưu thay đổi"}
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}
