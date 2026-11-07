import { AlertCircle, AlertTriangle, Check, Copy, ExternalLink, KeyRound, Pencil, Plus, RefreshCw, Store } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Badge, Button, EmptyState, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import {
  SIMULATOR_URL,
  readShopIntegrationError,
  shopIntegrationService,
  type ShopChannel,
  type ShopIntegration,
  type ShopIntegrationSecret,
} from "../services/shopIntegrationService";

type Branch = { id: string; name: string; isActive?: boolean };

const CHANNELS: { value: ShopChannel; label: string }[] = [
  { value: "Shopee", label: "Shopee" },
  { value: "TikTok", label: "TikTok Shop" },
  { value: "Lazada", label: "Lazada" },
];

const channelLabel = (c: string) => CHANNELS.find((x) => x.value === c)?.label ?? c;
const formatDate = (iso?: string | null) => (iso ? new Date(iso).toLocaleString("vi-VN") : "—");

const errorBox = { background: "#fef2f2", border: "1px solid #fecaca", borderRadius: "10px", color: "#b91c1c", padding: "10px 12px", fontSize: "13px", display: "flex", alignItems: "center", gap: "8px" } as const;

/**
 * Kết nối gian hàng sàn TMĐT (Owner). Secret webhook chỉ hiện một lần sau khi tạo hoặc đổi,
 * vì backend chỉ lưu bản băm.
 */
export function ShopIntegrationsPage() {
  const [items, setItems] = useState<ShopIntegration[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<ShopIntegration | null>(null);
  const [rotating, setRotating] = useState<ShopIntegration | null>(null);
  const [revealed, setRevealed] = useState<{ secret: ShopIntegrationSecret; reason: "created" | "rotated" } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const [list, branchRes] = await Promise.all([shopIntegrationService.list(), apiAdapter.getBranches()]);
      setItems(list);
      if ("data" in branchRes && Array.isArray(branchRes.data)) setBranches(branchRes.data);
    } catch (err) {
      setLoadError(readShopIntegrationError(err, "Không tải được danh sách kết nối sàn."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const activeBranches = useMemo(() => branches.filter((b) => b.isActive !== false), [branches]);

  const upsert = (integration: ShopIntegration) =>
    setItems((list) => {
      const exists = list.some((x) => x.id === integration.id);
      return exists ? list.map((x) => (x.id === integration.id ? integration : x)) : [...list, integration];
    });

  return (
    <>
      <PageTitle
        eyebrow="FR-SIM-01 · WEBHOOK SÀN TMĐT"
        title="Kết nối sàn"
        description="Mỗi gian hàng Shopee, TikTok Shop, Lazada có một secret riêng để gửi đơn vào OISMS qua webhook và gắn với một chi nhánh xử lý đơn."
        actions={
          <>
            {SIMULATOR_URL && (
              <a className="button button--secondary" href={SIMULATOR_URL} target="_blank" rel="noopener noreferrer">
                <ExternalLink size={17} />
                Mở app giả lập
              </a>
            )}
            <Button onClick={() => setCreateOpen(true)} disabled={loading}>
              <Plus size={17} />
              Tạo kết nối
            </Button>
          </>
        }
      />

      <section className="card" style={{ padding: "16px 20px", marginBottom: "16px" }}>
        <strong style={{ display: "flex", alignItems: "center", gap: "8px" }}>
          <KeyRound size={17} /> Cách lấy secret để bắn đơn giả lập
        </strong>
        <ol style={{ margin: "10px 0 0", paddingLeft: "22px", lineHeight: 1.7, color: "#475569", fontSize: "14px", listStyle: "decimal" }}>
          <li>Bấm <b>Tạo kết nối</b>, chọn sàn, nhập mã gian hàng và chi nhánh xử lý đơn.</li>
          <li>Copy secret ngay trong hộp thoại hiện ra. Secret chỉ hiện <b>một lần</b>; hệ thống chỉ lưu bản băm nên không xem lại được.</li>
          <li>Mở app giả lập, dán secret, app tự nhận ra sàn và mã gian hàng.</li>
          <li>Lỡ mất secret thì bấm <b>Đổi secret</b> để lấy secret mới. Secret cũ hết hiệu lực ngay.</li>
        </ol>
      </section>

      <section className="card">
        {loadError && (
          <div style={{ ...errorBox, margin: "16px", justifyContent: "space-between" }}>
            <span style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={18} />
              {loadError}
            </span>
            <Button variant="secondary" onClick={() => void load()}>
              <RefreshCw size={15} /> Thử lại
            </Button>
          </div>
        )}

        {!loading && !loadError && items.length === 0 ? (
          <EmptyState title="Chưa có kết nối sàn" description="Tạo kết nối đầu tiên để nhận secret và bắn đơn giả lập vào hệ thống." />
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Gian hàng</th>
                  <th>Chi nhánh xử lý đơn</th>
                  <th>Trạng thái</th>
                  <th>Cập nhật</th>
                  <th style={{ textAlign: "right" }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                      Đang tải danh sách kết nối...
                    </td>
                  </tr>
                ) : (
                  items.map((it) => (
                    <tr key={it.id}>
                      <td>
                        <div className="person-cell">
                          <div className="avatar" style={{ background: "#4f46e5" }}>
                            <Store size={16} />
                          </div>
                          <div>
                            <strong>{channelLabel(it.channel)}</strong>
                            <span style={{ fontFamily: "ui-monospace, monospace" }}>{it.externalShopId}</span>
                          </div>
                        </div>
                      </td>
                      <td>{it.branchName || "—"}</td>
                      <td>
                        <Badge tone={it.isActive ? "success" : "neutral"}>{it.isActive ? "Đang nhận đơn" : "Đã tạm dừng"}</Badge>
                      </td>
                      <td>{formatDate(it.updatedAt ?? it.createdAt)}</td>
                      <td style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                        <Button variant="ghost" onClick={() => setEditing(it)} aria-label={`Sửa kết nối ${it.externalShopId}`}>
                          <Pencil size={15} /> Sửa
                        </Button>
                        <Button variant="ghost" onClick={() => setRotating(it)} aria-label={`Đổi secret của ${it.externalShopId}`}>
                          <KeyRound size={15} /> Đổi secret
                        </Button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {createOpen && (
        <CreateModal
          branches={activeBranches}
          onClose={() => setCreateOpen(false)}
          onCreated={(result) => {
            upsert(result.integration);
            setCreateOpen(false);
            setRevealed({ secret: result, reason: "created" });
          }}
        />
      )}

      {editing && (
        <EditModal
          integration={editing}
          branches={branches}
          onClose={() => setEditing(null)}
          onSaved={(updated) => {
            upsert(updated);
            setEditing(null);
          }}
        />
      )}

      {rotating && (
        <RotateModal
          integration={rotating}
          onClose={() => setRotating(null)}
          onRotated={(result) => {
            upsert(result.integration);
            setRotating(null);
            setRevealed({ secret: result, reason: "rotated" });
          }}
        />
      )}

      {revealed && <SecretModal secret={revealed.secret} reason={revealed.reason} onClose={() => setRevealed(null)} />}
    </>
  );
}

function CreateModal({ branches, onClose, onCreated }: { branches: Branch[]; onClose: () => void; onCreated: (r: ShopIntegrationSecret) => void }) {
  const [channel, setChannel] = useState<ShopChannel>("Shopee");
  const [shopId, setShopId] = useState("");
  const [branchId, setBranchId] = useState(branches[0]?.id ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!shopId.trim()) return setError("Nhập mã gian hàng trên sàn.");
    if (shopId.trim().length > 100) return setError("Mã gian hàng tối đa 100 ký tự.");
    if (!branchId) return setError("Chọn chi nhánh xử lý đơn.");
    setSaving(true);
    setError(null);
    try {
      onCreated(await shopIntegrationService.create({ channel, externalShopId: shopId.trim(), branchId }));
    } catch (err) {
      setError(readShopIntegrationError(err, "Không tạo được kết nối."));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title="Tạo kết nối sàn" onClose={onClose}>
      {error && <div style={{ ...errorBox, margin: "16px 20px 0" }}><AlertCircle size={16} />{error}</div>}
      <div className="form-grid">
        <label>
          <span>Sàn *</span>
          <Select value={channel} onChange={(e) => setChannel(e.target.value as ShopChannel)}>
            {CHANNELS.map((c) => (
              <option key={c.value} value={c.value}>{c.label}</option>
            ))}
          </Select>
        </label>
        <label>
          <span>Mã gian hàng trên sàn *</span>
          <Input value={shopId} onChange={(e) => setShopId(e.target.value)} placeholder="Ví dụ: SHOP_DEMO_1" maxLength={100} />
        </label>
        <label className="full-field">
          <span>Chi nhánh xử lý đơn *</span>
          {branches.length === 0 ? (
            <span style={{ color: "#b45309", fontSize: "13px" }}>Chưa có chi nhánh đang hoạt động. Tạo chi nhánh ở mục Nhân viên & chi nhánh trước.</span>
          ) : (
            <Select value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </Select>
          )}
        </label>
      </div>
      <div className="modal-actions">
        <Button variant="secondary" onClick={onClose}>Hủy</Button>
        <Button onClick={() => void submit()} disabled={saving || branches.length === 0}>
          {saving ? "Đang tạo..." : "Tạo và lấy secret"}
        </Button>
      </div>
    </Modal>
  );
}

function EditModal({ integration, branches, onClose, onSaved }: { integration: ShopIntegration; branches: Branch[]; onClose: () => void; onSaved: (i: ShopIntegration) => void }) {
  const [branchId, setBranchId] = useState(integration.branchId);
  const [isActive, setIsActive] = useState(integration.isActive);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Giữ chi nhánh hiện tại trong danh sách dù đã tắt, để không tự đổi khi Owner chỉ bật/tắt kết nối.
  const options = branches.filter((b) => b.isActive !== false || b.id === integration.branchId);

  const submit = async () => {
    setSaving(true);
    setError(null);
    try {
      onSaved(await shopIntegrationService.update(integration.id, { branchId, isActive }));
    } catch (err) {
      setError(readShopIntegrationError(err, "Không lưu được kết nối."));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={`Sửa kết nối ${channelLabel(integration.channel)} · ${integration.externalShopId}`} onClose={onClose}>
      {error && <div style={{ ...errorBox, margin: "16px 20px 0" }}><AlertCircle size={16} />{error}</div>}
      <div className="form-grid">
        <label className="full-field">
          <span>Chi nhánh xử lý đơn</span>
          <Select value={branchId} onChange={(e) => setBranchId(e.target.value)}>
            {options.map((b) => (
              <option key={b.id} value={b.id}>{b.name}{b.isActive === false ? " (đã tắt)" : ""}</option>
            ))}
          </Select>
        </label>
        <label className="full-field" style={{ display: "flex", alignItems: "center", gap: "8px", flexDirection: "row" }}>
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          <span style={{ margin: 0 }}>Đang nhận đơn. Bỏ chọn thì webhook dùng secret này trả 401.</span>
        </label>
      </div>
      <div className="modal-actions">
        <Button variant="secondary" onClick={onClose}>Hủy</Button>
        <Button onClick={() => void submit()} disabled={saving}>{saving ? "Đang lưu..." : "Lưu"}</Button>
      </div>
    </Modal>
  );
}

function RotateModal({ integration, onClose, onRotated }: { integration: ShopIntegration; onClose: () => void; onRotated: (r: ShopIntegrationSecret) => void }) {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSaving(true);
    setError(null);
    try {
      onRotated(await shopIntegrationService.rotateSecret(integration.id));
    } catch (err) {
      setError(readShopIntegrationError(err, "Không đổi được secret."));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title="Đổi secret" onClose={onClose}>
      <div style={{ padding: "16px 20px", fontSize: "14px", lineHeight: 1.6 }}>
        {error && <div style={{ ...errorBox, marginBottom: "12px" }}><AlertCircle size={16} />{error}</div>}
        <p style={{ margin: 0 }}>
          Tạo secret mới cho <b>{channelLabel(integration.channel)} · {integration.externalShopId}</b>.
        </p>
        <p style={{ margin: "8px 0 0", color: "#b45309", display: "flex", gap: "8px" }}>
          <AlertTriangle size={18} style={{ flexShrink: 0 }} />
          Secret cũ hết hiệu lực ngay. Nơi nào đang dùng secret cũ (app giả lập, script) sẽ nhận 401 cho đến khi dán secret mới.
        </p>
      </div>
      <div className="modal-actions">
        <Button variant="secondary" onClick={onClose}>Hủy</Button>
        <Button variant="danger" onClick={() => void submit()} disabled={saving}>{saving ? "Đang đổi..." : "Đổi secret"}</Button>
      </div>
    </Modal>
  );
}

function SecretModal({ secret, reason, onClose }: { secret: ShopIntegrationSecret; reason: "created" | "rotated"; onClose: () => void }) {
  const [copied, setCopied] = useState(false);
  const [saved, setSaved] = useState(false);
  const [nudge, setNudge] = useState(false);
  const { integration, webhookSecret } = secret;

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(webhookSecret);
      setCopied(true);
      setSaved(true);
    } catch {
      // Trình duyệt chặn clipboard: người dùng tự bôi đen ô secret để copy.
      setCopied(false);
    }
  };

  // Không cho đóng khi chưa xác nhận đã lưu, vì đóng rồi là không xem lại được.
  const close = () => (saved ? onClose() : setNudge(true));

  return (
    <Modal title={reason === "created" ? "Đã tạo kết nối" : "Đã đổi secret"} onClose={close}>
      <div style={{ padding: "16px 20px", fontSize: "14px", lineHeight: 1.6 }}>
        <p style={{ margin: 0 }}>
          Secret của <b>{channelLabel(integration.channel)} · {integration.externalShopId}</b> (chi nhánh {integration.branchName || "—"}):
        </p>
        <div style={{ display: "flex", gap: "8px", marginTop: "10px" }}>
          <input
            className="input"
            readOnly
            value={webhookSecret}
            onFocus={(e) => e.currentTarget.select()}
            style={{ fontFamily: "ui-monospace, monospace", fontSize: "13px" }}
            aria-label="Webhook secret"
          />
          <Button variant="secondary" onClick={() => void copy()}>
            {copied ? <Check size={16} /> : <Copy size={16} />}
            {copied ? "Đã copy" : "Copy"}
          </Button>
        </div>
        <p style={{ margin: "12px 0 0", color: "#b45309", display: "flex", gap: "8px" }}>
          <AlertTriangle size={18} style={{ flexShrink: 0 }} />
          Secret chỉ hiện một lần. Đóng hộp thoại này là không xem lại được; khi đó phải đổi secret mới.
        </p>
        <label style={{ display: "flex", alignItems: "center", gap: "8px", marginTop: "12px" }}>
          <input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
          Tôi đã lưu secret
        </label>
        {nudge && !saved && <p style={{ margin: "8px 0 0", color: "#b91c1c" }}>Copy hoặc đánh dấu "Tôi đã lưu secret" trước khi đóng.</p>}
        {SIMULATOR_URL && (
          <p style={{ margin: "12px 0 0", color: "#475569" }}>
            Dán secret vào{" "}
            <a href={SIMULATOR_URL} target="_blank" rel="noopener noreferrer">app giả lập</a>{" "}
            để bắn đơn thử.
          </p>
        )}
      </div>
      <div className="modal-actions">
        <Button onClick={close} disabled={!saved}>Xong</Button>
      </div>
    </Modal>
  );
}
