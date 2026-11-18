import { useEffect, useRef, useState, type FormEvent } from "react";
import { generateUuid } from "../../services/apiAdapter";
import { purchaseOrderService } from "../../services/purchaseOrderService";
import { readSupplierInvoiceError, supplierInvoiceService } from "../../services/supplierInvoiceService";
import type { GoodsReceiptResponse } from "../../types/goodsReceipt";
import type { CreateSupplierInvoice, SupplierInvoice, SupplierInvoiceDraft } from "../../types/supplierInvoice";
import { Button } from "../ui";
import { SupplierInvoiceDetails } from "./SupplierInvoiceDetails";
import { SupplierInvoiceForm } from "./SupplierInvoiceForm";
import "./supplierInvoices.css";

interface CreateAttempt {
  key: string;
  body: CreateSupplierInvoice;
}

interface FormData {
  mode: "create" | "edit";
  initial: SupplierInvoiceDraft;
  prices: Map<string, number>;
}

interface Props {
  receipt: GoodsReceiptResponse;
  actor: string;
  isOwner: boolean;
  receiptBusy: boolean;
  onBusyChange: (busy: boolean) => void;
  onRefreshReceipt: (updated?: SupplierInvoice) => Promise<void>;
}

function readAttempt(storageKey: string): CreateAttempt | null {
  try {
    const raw = sessionStorage.getItem(storageKey);
    return raw ? JSON.parse(raw) as CreateAttempt : null;
  } catch {
    return null;
  }
}

function storeAttempt(storageKey: string, attempt: CreateAttempt | null) {
  try {
    if (attempt) sessionStorage.setItem(storageKey, JSON.stringify(attempt));
    else sessionStorage.removeItem(storageKey);
  } catch {
    // The component still retains the attempt while the modal stays open.
  }
}

function invoiceToday() {
  return new Intl.DateTimeFormat("sv-SE", { timeZone: "Asia/Ho_Chi_Minh" }).format(new Date());
}

export function SupplierInvoicePanel({ receipt, actor, isOwner, receiptBusy, onBusyChange, onRefreshReceipt }: Props) {
  const storageKey = `oism_supplier_invoice_create:${actor}:${receipt.id}`;
  const attempt = useRef(readAttempt(storageKey));
  const busyRef = useRef(false);
  const [busy, setBusy] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [form, setForm] = useState<FormData | null>(null);
  const [action, setAction] = useState<"reject" | "cancel" | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [latest, setLatest] = useState<SupplierInvoice | null>(null);
  const [loaded, setLoaded] = useState<{ lookup: string; revision: number; invoice: SupplierInvoice | null; error?: string } | null>(null);
  const lookup = receipt.invoiceId ?? (receipt.status === "Cancelled" ? "rejected" : null);

  useEffect(() => {
    if (!lookup) return;
    let active = true;
    const load = async () => {
      try {
        const id = lookup === "rejected"
          ? (await supplierInvoiceService.list({ goodsReceiptId: receipt.id, status: "Rejected", pageSize: 1 })).items[0]?.id
          : lookup;
        const invoice = id ? await supplierInvoiceService.get(id) : null;
        if (!active) return;
        if (invoice && attempt.current?.key === invoice.requestKey) {
          attempt.current = null;
          storeAttempt(storageKey, null);
        }
        setLoaded({ lookup, revision, invoice });
      } catch (err) {
        if (active) setLoaded({ lookup, revision, invoice: null, error: readSupplierInvoiceError(err, "Không thể tải hóa đơn NCC.").message });
      }
    };
    void load();
    return () => { active = false; };
  }, [lookup, receipt.id, revision, storageKey]);

  const current = loaded?.lookup === lookup && loaded.revision === revision ? loaded : null;
  const terminal = latest?.status === "Cancelled" || latest?.status === "Rejected";
  const invoice = current?.invoice ?? (
    latest && ((latest.id === lookup && !current) || (!receipt.invoiceId && terminal)) ? latest : null
  );
  const waiting = Boolean(lookup && !current && !invoice);
  const canCreate = !receipt.invoiceId && receipt.status !== "Cancelled"
    && Boolean(receipt.purchaseOrderId) && receipt.items.length > 0
    && receipt.items.every((item) => Boolean(item.purchaseOrderItemId));

  async function prepareCreate() {
    if (!receipt.purchaseOrderId || preparing || busy) return;
    setError("");
    setPreparing(true);
    try {
      const po = await purchaseOrderService.getPurchaseOrderById(receipt.purchaseOrderId);
      const poPrices = new Map(po.items.map((item) => [item.id, item.unitPrice]));
      const prices = new Map<string, number>();
      const items = receipt.items.map((item) => {
        const price = poPrices.get(item.purchaseOrderItemId ?? "");
        if (price === undefined) throw new Error("Phiếu nhập thiếu dòng phiếu mua tương ứng.");
        prices.set(item.id, price);
        return { goodsReceiptItemId: item.id, quantity: item.quantity, unitPrice: price };
      });
      setForm({
        mode: "create",
        prices,
        initial: attempt.current?.body ?? {
          invoiceSeries: "", invoiceNumber: "", invoiceDate: invoiceToday(), vatAmount: 0, shippingFee: 0, items,
        },
      });
    } catch (err) {
      const fallback = err instanceof Error && !("response" in err) ? err.message : "Không thể tải đơn giá từ phiếu mua.";
      setError(readSupplierInvoiceError(err, fallback).message);
    } finally {
      setPreparing(false);
    }
  }

  function prepareEdit(draft: SupplierInvoice) {
    const prices = new Map<string, number>();
    const items = (draft.comparison ?? []).flatMap((line) => {
      if (line.purchaseOrder.unitPrice != null) prices.set(line.goodsReceiptItemId, line.purchaseOrder.unitPrice);
      return line.invoice?.unitPrice == null ? [] : [{
        goodsReceiptItemId: line.goodsReceiptItemId,
        quantity: line.invoice.quantity,
        unitPrice: line.invoice.unitPrice,
      }];
    });
    setError("");
    setForm({
      mode: "edit", prices,
      initial: {
        invoiceSeries: draft.invoiceSeries, invoiceNumber: draft.invoiceNumber, invoiceDate: draft.invoiceDate,
        vatAmount: draft.vatAmount, shippingFee: draft.shippingFee, items,
      },
    });
  }

  async function mutate(request: () => Promise<SupplierInvoice>, fallback: string, creating = false) {
    if (busyRef.current || receiptBusy) return;
    busyRef.current = true;
    setBusy(true);
    onBusyChange(true);
    setError("");
    try {
      const updated = await request();
      if (creating) {
        attempt.current = null;
        storeAttempt(storageKey, null);
      }
      setLatest(updated);
      if (updated.status !== "Cancelled" && updated.status !== "Rejected") {
        setLoaded({ lookup: updated.id, revision, invoice: updated });
      }
      setForm(null);
      setAction(null);
      setReason("");
      await onRefreshReceipt(updated);
    } catch (err) {
      const failure = readSupplierInvoiceError(err, fallback);
      if (creating && !failure.uncertain) {
        attempt.current = null;
        storeAttempt(storageKey, null);
      }
      setError(failure.message);
      if (failure.code === "SUPPLIER_INVOICE_INVALID_STATE" || failure.code === "SUPPLIER_INVOICE_ALREADY_EXISTS") {
        setForm(null);
        setAction(null);
        await onRefreshReceipt();
        setRevision((value) => value + 1);
      }
    } finally {
      busyRef.current = false;
      setBusy(false);
      onBusyChange(false);
    }
  }

  function saveDraft(draft: SupplierInvoiceDraft) {
    if (busyRef.current || receiptBusy) return;
    if (form?.mode === "edit" && invoice?.status === "Draft") {
      void mutate(() => supplierInvoiceService.update(invoice.id, draft), "Không thể lưu thay đổi hóa đơn.");
      return;
    }
    if (form?.mode !== "create" || !canCreate) return;

    const pending = attempt.current ?? {
      key: generateUuid(),
      body: { ...draft, goodsReceiptId: receipt.id, supplierId: receipt.supplierId },
    };
    attempt.current = pending;
    storeAttempt(storageKey, pending);
    void mutate(() => supplierInvoiceService.create(pending.body, pending.key), "Chưa xác minh được kết quả lưu hóa đơn.", true);
  }

  function chooseAction(value: "reject" | "cancel") {
    setError("");
    setReason("");
    setAction(value);
  }

  function submitReason(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!action || !invoice) return;
    const note = reason.trim();
    if (!note) {
      setError(action === "reject" ? "Vui lòng nhập lý do từ chối." : "Vui lòng nhập lý do hủy.");
      return;
    }
    const request = action === "reject"
      ? () => supplierInvoiceService.reject(invoice.id, note)
      : () => supplierInvoiceService.cancel(invoice.id, note);
    void mutate(request, action === "reject" ? "Không thể từ chối chênh lệch." : "Không thể hủy hóa đơn.");
  }

  const showForm = form && (form.mode === "create" ? canCreate : invoice?.status === "Draft");
  const actionsDisabled = busy || receiptBusy || preparing;
  const canDecide = isOwner && invoice?.status === "PendingApproval";
  const canCancel = invoice?.status === "Draft" || invoice?.status === "Matched" || invoice?.status === "Approved";

  return (
    <section className="supplier-invoice" aria-label="Hóa đơn nhà cung cấp">
      <div className="supplier-invoice__bar">
        <h3>Hóa đơn nhà cung cấp</h3>
        {canCreate && !showForm && (
          <Button type="button" variant="secondary" disabled={actionsDisabled} onClick={prepareCreate}>
            {preparing ? "Đang tải giá phiếu mua..." : "Nhập hóa đơn NCC"}
          </Button>
        )}
      </div>
      {error && <p role="alert" className="supplier-invoice__error">{error}</p>}
      {waiting && <p role="status">Đang tải hóa đơn NCC...</p>}
      {current?.error && (
        <div className="supplier-invoice__actions">
          <p role="alert" className="supplier-invoice__error">{current.error}</p>
          <Button type="button" variant="secondary" onClick={() => setRevision((value) => value + 1)}>Thử tải lại hóa đơn</Button>
        </div>
      )}
      {!lookup && !invoice && !showForm && !preparing && (
        <p>{canCreate ? "Chưa có hóa đơn NCC còn hiệu lực." : "Phiếu nhập thiếu liên kết hợp lệ với phiếu mua."}</p>
      )}
      {lookup === "rejected" && current && !invoice && !current.error && <p>Chưa có hóa đơn NCC bị từ chối cho phiếu này.</p>}
      {showForm ? (
        <SupplierInvoiceForm
          key={form.mode}
          initial={form.initial}
          receiptItems={receipt.items}
          purchasePrices={form.prices}
          busy={busy}
          retrying={form.mode === "create" && attempt.current != null}
          onSubmit={saveDraft}
          onClose={() => { setForm(null); setError(""); }}
        />
      ) : invoice && (
        <>
          <SupplierInvoiceDetails invoice={invoice} items={receipt.items} />
          {busy && <p role="status">Đang cập nhật hóa đơn...</p>}
          {action ? (
            <form className="supplier-invoice__form" onSubmit={submitReason}>
              <label className="supplier-invoice__reason">
                {action === "reject" ? "Lý do từ chối" : "Lý do hủy hóa đơn"}
                <textarea className="input" required maxLength={500} rows={3} value={reason} readOnly={busy} onChange={(e) => setReason(e.target.value)} />
              </label>
              <div className="supplier-invoice__actions">
                <Button type="submit" variant="danger" disabled={actionsDisabled || !reason.trim()}>
                  {action === "reject" ? "Xác nhận từ chối" : "Xác nhận hủy hóa đơn"}
                </Button>
                <Button type="button" variant="secondary" disabled={busy} onClick={() => { setAction(null); setError(""); }}>Quay lại</Button>
              </div>
            </form>
          ) : (
            <div className="supplier-invoice__actions">
              {invoice.status === "Draft" && receipt.status !== "Cancelled" && (
                <>
                  <Button type="button" variant="secondary" disabled={actionsDisabled} onClick={() => prepareEdit(invoice)}>Sửa nháp</Button>
                  <Button type="button" disabled={actionsDisabled} onClick={() => void mutate(() => supplierInvoiceService.submit(invoice.id), "Không thể gửi đối chiếu.")}>Gửi đối chiếu</Button>
                </>
              )}
              {canDecide && (
                <>
                  <Button type="button" disabled={actionsDisabled} onClick={() => void mutate(() => supplierInvoiceService.approve(invoice.id), "Không thể duyệt chênh lệch.")}>Duyệt chênh lệch</Button>
                  <Button type="button" variant="danger" disabled={actionsDisabled} onClick={() => chooseAction("reject")}>Từ chối</Button>
                </>
              )}
              {canCancel && <Button type="button" variant="secondary" disabled={actionsDisabled} onClick={() => chooseAction("cancel")}>Hủy hóa đơn</Button>}
            </div>
          )}
        </>
      )}
    </section>
  );
}
