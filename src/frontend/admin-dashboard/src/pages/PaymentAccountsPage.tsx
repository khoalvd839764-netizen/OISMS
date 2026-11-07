import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  Plus,
  AlertCircle,
  Eye,
  Star,
  PowerOff,
  RefreshCw,
  Landmark,
} from 'lucide-react';
import { PageTitle, Button, Badge, Modal, Input, EmptyState } from '../components/ui';
import {
  paymentAccountsApi,
  fetchVietQrBanks,
  type PaymentAccount,
  type VietQrBankItem,
  FALLBACK_BANKS,
} from '../services/paymentAccountsApi';

export function PaymentAccountsPage() {
  const [accounts, setAccounts] = useState<PaymentAccount[]>([]);
  const [banks, setBanks] = useState<VietQrBankItem[]>(FALLBACK_BANKS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal states
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [previewAccount, setPreviewAccount] = useState<PaymentAccount | null>(null);

  // Form states
  const [selectedBankBin, setSelectedBankBin] = useState<string>(FALLBACK_BANKS[0].bin);
  const [accountNumber, setAccountNumber] = useState('');
  const [accountName, setAccountName] = useState('');
  const [isDefault, setIsDefault] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [accList, bankList] = await Promise.all([
        paymentAccountsApi.list(),
        fetchVietQrBanks(),
      ]);
      setAccounts(accList);
      if (bankList.length > 0) {
        setBanks(bankList);
        if (!selectedBankBin) {
          setSelectedBankBin(bankList[0].bin);
        }
      }
    } catch (err: any) {
      setError(err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải danh sách tài khoản.');
    } finally {
      setLoading(false);
    }
  }, [selectedBankBin]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const selectedBank = useMemo(() => {
    return banks.find((b) => b.bin === selectedBankBin) || banks[0];
  }, [banks, selectedBankBin]);

  const handleOpenCreateModal = () => {
    setAccountNumber('');
    setAccountName('');
    setIsDefault(accounts.length === 0);
    setFormError(null);
    setCreateModalOpen(true);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    const cleanAccNumber = accountNumber.trim();
    const cleanAccName = accountName.trim().toUpperCase();

    if (!cleanAccNumber || cleanAccNumber.length < 6 || cleanAccNumber.length > 19) {
      setFormError('Số tài khoản phải từ 6 đến 19 ký tự.');
      return;
    }
    if (!cleanAccName || cleanAccName.length < 2) {
      setFormError('Tên chủ tài khoản không được để trống.');
      return;
    }

    setIsSubmitting(true);
    setFormError(null);

    try {
      await paymentAccountsApi.create({
        bankBin: selectedBank.bin,
        bankName: selectedBank.name,
        bankShortName: selectedBank.shortName,
        accountNumber: cleanAccNumber,
        accountName: cleanAccName,
        isDefault,
      });
      setCreateModalOpen(false);
      await loadData();
    } catch (err: any) {
      setFormError(err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tạo tài khoản ngân hàng.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSetDefault = async (account: PaymentAccount) => {
    if (account.isDefault) return;
    try {
      await paymentAccountsApi.setDefault(account.id);
      await loadData();
    } catch (err: any) {
      alert(err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể đặt tài khoản mặc định.');
    }
  };

  const handleDeactivate = async (account: PaymentAccount) => {
    if (!confirm(`Bạn có chắc muốn ngừng sử dụng tài khoản ${account.bankShortName} - ${account.accountNumber}?`)) {
      return;
    }
    try {
      await paymentAccountsApi.deactivate(account.id);
      await loadData();
    } catch (err: any) {
      alert(err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể ngừng sử dụng tài khoản.');
    }
  };

  return (
    <>
      <PageTitle
        eyebrow="CS-05 · THANH TOÁN VIETQR"
        title="Tài khoản nhận tiền"
        description="Cấu hình tài khoản ngân hàng thụ hưởng nhận tiền theo shop. Thu ngân quầy POS sẽ dùng tài khoản này để tạo mã VietQR động."
        actions={
          <Button onClick={handleOpenCreateModal} disabled={loading}>
            <Plus size={17} />
            Thêm tài khoản
          </Button>
        }
      />

      <section className="card" style={{ padding: '16px 20px', marginBottom: '16px' }}>
        <strong style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <Landmark size={17} /> Quy tắc quản lý tài khoản thụ hưởng POS
        </strong>
        <ol style={{ margin: '10px 0 0', paddingLeft: '22px', lineHeight: 1.7, color: '#475569', fontSize: '14px', listStyle: 'decimal' }}>
          <li>Tài khoản nhận tiền áp dụng chung cho toàn bộ cửa hàng (shop-level). Thu ngân quầy POS có thể chọn tài khoản khi khách thanh toán.</li>
          <li>Mỗi shop cần có đúng <b>một tài khoản mặc định</b>. Tài khoản mặc định sẽ được chọn sẵn khi mở cửa sổ thanh toán QR.</li>
          <li>Mã VietQR được sinh động với định dạng QuickLink chuẩn NAPAS 247, kèm số tiền và mã tham chiếu giao dịch.</li>
          <li>Tài khoản đã từng phát sinh giao dịch thanh toán không thể xóa cứng để bảo toàn sổ sách kế toán; chỉ có thể <b>ngừng dùng</b>.</li>
        </ol>
      </section>

      {error && (
        <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '10px', color: '#b91c1c', padding: '12px 16px', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
          <AlertCircle size={18} />
          <span>{error}</span>
          <Button style={{ marginLeft: 'auto' }} variant="ghost" onClick={() => void loadData()}>
            <RefreshCw size={14} /> Thử lại
          </Button>
        </div>
      )}

      {loading ? (
        <div className="card" style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
          Đang tải danh sách tài khoản nhận tiền...
        </div>
      ) : accounts.length === 0 ? (
        <div className="card" style={{ padding: '24px', textAlign: 'center' }}>
          <EmptyState
            title="Chưa có tài khoản nhận tiền nào"
            description="Thêm tài khoản ngân hàng thụ hưởng để bật phương thức thanh toán VietQR tại quầy POS."
          />
          <div style={{ marginTop: '16px' }}>
            <Button onClick={handleOpenCreateModal}>
              <Plus size={16} /> Thêm tài khoản đầu tiên
            </Button>
          </div>
        </div>
      ) : (
        <div className="card" style={{ overflow: 'hidden' }}>
          <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0', textAlign: 'left', fontSize: '12px', color: '#475569' }}>
                <th style={{ padding: '12px 16px' }}>Ngân hàng</th>
                <th style={{ padding: '12px 16px' }}>Số tài khoản</th>
                <th style={{ padding: '12px 16px' }}>Chủ tài khoản</th>
                <th style={{ padding: '12px 16px' }}>Trạng thái</th>
                <th style={{ padding: '12px 16px', textAlign: 'right' }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {accounts.map((acc) => (
                <tr key={acc.id} style={{ borderBottom: '1px solid #f1f5f9', fontSize: '13px' }}>
                  <td style={{ padding: '14px 16px' }}>
                    <div style={{ fontWeight: 600, color: '#0f172a' }}>{acc.bankShortName}</div>
                    <div style={{ fontSize: '11px', color: '#64748b' }}>
                      {acc.bankName} (BIN: {acc.bankBin})
                    </div>
                  </td>
                  <td style={{ padding: '14px 16px', fontFamily: 'monospace', fontWeight: 600, color: '#1e293b' }}>
                    {acc.accountNumber}
                  </td>
                  <td style={{ padding: '14px 16px', fontWeight: 600, color: '#334155' }}>
                    {acc.accountName}
                  </td>
                  <td style={{ padding: '14px 16px' }}>
                    <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                      {acc.isDefault && (
                        <Badge tone="success">★ Mặc định</Badge>
                      )}
                      {acc.isActive ? (
                        <Badge tone="neutral">Hoạt động</Badge>
                      ) : (
                        <Badge tone="warning">Đã tắt</Badge>
                      )}
                    </div>
                  </td>
                  <td style={{ padding: '14px 16px', textAlign: 'right' }}>
                    <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', alignItems: 'center' }}>
                      <Button
                        variant="ghost"
                        onClick={() => setPreviewAccount(acc)}
                        title="Xem thử mã QR"
                      >
                        <Eye size={14} /> Xem QR
                      </Button>
                      {acc.isActive && !acc.isDefault && (
                        <Button
                          variant="ghost"
                          onClick={() => void handleSetDefault(acc)}
                          title="Đặt làm tài khoản mặc định"
                        >
                          <Star size={14} /> Đặt mặc định
                        </Button>
                      )}
                      {acc.isActive && (
                        <Button
                          variant="ghost"
                          onClick={() => void handleDeactivate(acc)}
                          title="Ngừng sử dụng"
                          style={{ color: '#e11d48' }}
                        >
                          <PowerOff size={14} /> Ngừng dùng
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Modal Thêm tài khoản */}
      {createModalOpen && (
        <Modal
          onClose={() => setCreateModalOpen(false)}
          title="Thêm tài khoản ngân hàng thụ hưởng"
        >
          <form onSubmit={handleCreate} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            {formError && (
              <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', padding: '10px 12px', fontSize: '13px' }}>
                {formError}
              </div>
            )}

            <div>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                Ngân hàng thụ hưởng *
              </label>
              <select
                value={selectedBankBin}
                onChange={(e) => setSelectedBankBin(e.target.value)}
                style={{ width: '100%', padding: '10px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', fontSize: '14px', background: '#fff' }}
              >
                {banks.map((b) => (
                  <option key={b.bin} value={b.bin}>
                    {b.shortName} - {b.name} (BIN {b.bin})
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                Số tài khoản *
              </label>
              <Input
                value={accountNumber}
                onChange={(e) => setAccountNumber(e.target.value.replace(/[^0-9a-zA-Z]/g, ''))}
                placeholder="Ví dụ: 123456789"
                maxLength={19}
                required
              />
              <div style={{ fontSize: '11px', color: '#64748b', marginTop: '4px' }}>
                Từ 6 đến 19 ký tự số hoặc chữ theo chuẩn NAPAS.
              </div>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                Tên chủ tài khoản *
              </label>
              <Input
                value={accountName}
                onChange={(e) => setAccountName(e.target.value.toUpperCase())}
                placeholder="Ví dụ: NGUYEN VAN A"
                required
              />
              <div style={{ fontSize: '11px', color: '#64748b', marginTop: '4px' }}>
                Tên in hoa trên thẻ/tài khoản ngân hàng.
              </div>
            </div>

            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', color: '#334155', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={isDefault}
                onChange={(e) => setIsDefault(e.target.checked)}
              />
              <span>Đặt làm tài khoản mặc định cho quầy bán hàng POS</span>
            </label>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
              <Button type="button" variant="ghost" onClick={() => setCreateModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? 'Đang lưu...' : 'Lưu tài khoản'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal Xem thử mã QR */}
      {previewAccount && (
        <Modal
          onClose={() => setPreviewAccount(null)}
          title="Xem thử mã QR VietQR POS"
        >
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', gap: '12px' }}>
            <div style={{ background: '#fef3c7', border: '1px solid #fde68a', color: '#92400e', padding: '6px 12px', borderRadius: '6px', fontSize: '11px', fontWeight: 600 }}>
              DỮ LIỆU KIỂM THỬ — Mẫu QR kiểm tra thông tin thụ hưởng
            </div>
            <div style={{ background: '#fff', padding: '12px', borderRadius: '12px', border: '1px solid #e2e8f0', boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)' }}>
              <img
                src={`https://img.vietqr.io/image/${previewAccount.bankBin}-${previewAccount.accountNumber}-compact2.png?amount=100000&addInfo=TEST%20OISMS%20POS&accountName=${encodeURIComponent(previewAccount.accountName)}`}
                alt="VietQR Preview"
                style={{ width: '220px', height: '220px', objectFit: 'contain', display: 'block' }}
              />
            </div>
            <div style={{ fontSize: '13px', color: '#1e293b', lineHeight: 1.5 }}>
              <div>Ngân hàng: <strong>{previewAccount.bankShortName}</strong> (BIN: {previewAccount.bankBin})</div>
              <div>Số TK: <strong style={{ fontFamily: 'monospace' }}>{previewAccount.accountNumber}</strong></div>
              <div>Chủ TK: <strong>{previewAccount.accountName}</strong></div>
            </div>
            <Button onClick={() => setPreviewAccount(null)} style={{ marginTop: '8px' }}>
              Đóng lại
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}
