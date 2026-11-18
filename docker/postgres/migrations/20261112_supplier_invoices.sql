-- Upgrade 01-B: supplier invoices and three-way matching. Run as schema owner with psql.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_supplier_invoice_schema_upgrade'));

CREATE TABLE IF NOT EXISTS public.supplier_invoices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    goods_receipt_id uuid NOT NULL,
    supplier_id uuid NOT NULL,
    invoice_series text NOT NULL CHECK (length(btrim(invoice_series)) BETWEEN 1 AND 50),
    invoice_number text NOT NULL CHECK (length(btrim(invoice_number)) BETWEEN 1 AND 50),
    invoice_date date NOT NULL,
    subtotal numeric NOT NULL CHECK (subtotal >= 0),
    vat_amount numeric NOT NULL CHECK (vat_amount >= 0),
    shipping_fee numeric NOT NULL CHECK (shipping_fee >= 0),
    total_amount numeric NOT NULL CHECK (total_amount = subtotal + vat_amount + shipping_fee),
    status text NOT NULL,
    request_key uuid NOT NULL,
    request_payload_hash text NOT NULL,
    created_by uuid NOT NULL,
    submitted_at timestamptz,
    cancelled_by uuid,
    cancelled_at timestamptz,
    cancel_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_supplier_invoices_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_supplier_invoices_request_key UNIQUE (tenant_id, request_key),
    CONSTRAINT ck_supplier_invoices_status CHECK (status IN ('Draft','Matched','PendingApproval','Approved','Rejected','Cancelled')),
    CONSTRAINT ck_supplier_invoices_submitted CHECK (
        (status = 'Draft' AND submitted_at IS NULL) OR status = 'Cancelled'
        OR (status <> 'Draft' AND submitted_at IS NOT NULL)),
    CONSTRAINT ck_supplier_invoices_cancelled CHECK (
        (status = 'Cancelled' AND cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL
            AND cancel_reason IS NOT NULL AND length(btrim(cancel_reason)) BETWEEN 1 AND 500)
        OR (status <> 'Cancelled' AND cancelled_at IS NULL AND cancelled_by IS NULL AND cancel_reason IS NULL)),
    CONSTRAINT fk_supplier_invoices_tenant FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoices_receipt FOREIGN KEY (tenant_id, goods_receipt_id) REFERENCES public.goods_receipts (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoices_supplier FOREIGN KEY (tenant_id, supplier_id) REFERENCES public.suppliers (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoices_creator FOREIGN KEY (tenant_id, created_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoices_canceller FOREIGN KEY (tenant_id, cancelled_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_supplier_invoices_number
    ON public.supplier_invoices (tenant_id, supplier_id, invoice_series, invoice_number)
    WHERE status <> 'Cancelled';
CREATE UNIQUE INDEX IF NOT EXISTS uq_supplier_invoices_active_receipt
    ON public.supplier_invoices (tenant_id, goods_receipt_id)
    WHERE status NOT IN ('Cancelled','Rejected');
CREATE INDEX IF NOT EXISTS ix_supplier_invoices_date
    ON public.supplier_invoices (tenant_id, invoice_date DESC, id);
CREATE INDEX IF NOT EXISTS ix_supplier_invoices_receipt
    ON public.supplier_invoices (tenant_id, goods_receipt_id);
CREATE INDEX IF NOT EXISTS ix_supplier_invoices_supplier
    ON public.supplier_invoices (tenant_id, supplier_id);

CREATE TABLE IF NOT EXISTS public.supplier_invoice_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    goods_receipt_item_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    quantity numeric(18,3) NOT NULL CHECK (quantity >= 0),
    unit_price numeric(20,6) NOT NULL CHECK (unit_price >= 0),
    expected_quantity numeric(18,3) CHECK (expected_quantity >= 0),
    expected_unit_price numeric(20,6) CHECK (expected_unit_price >= 0),
    quantity_diff numeric(18,3),
    price_diff numeric(20,6),
    amount_diff numeric,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_supplier_invoice_items_receipt_item UNIQUE (tenant_id, invoice_id, goods_receipt_item_id),
    CONSTRAINT ck_supplier_invoice_items_comparison CHECK (
        num_nonnulls(expected_quantity, expected_unit_price, quantity_diff, price_diff, amount_diff) IN (0, 5)),
    CONSTRAINT fk_supplier_invoice_items_tenant FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoice_items_invoice FOREIGN KEY (tenant_id, invoice_id) REFERENCES public.supplier_invoices (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoice_items_receipt_item FOREIGN KEY (tenant_id, goods_receipt_item_id) REFERENCES public.goods_receipt_items (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_supplier_invoice_items_sku FOREIGN KEY (tenant_id, sku_id) REFERENCES public.skus (tenant_id, id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_supplier_invoice_items_receipt_item
    ON public.supplier_invoice_items (tenant_id, goods_receipt_item_id);
CREATE INDEX IF NOT EXISTS ix_supplier_invoice_items_sku
    ON public.supplier_invoice_items (tenant_id, sku_id);

CREATE TABLE IF NOT EXISTS public.purchase_variances (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    goods_receipt_id uuid NOT NULL,
    variance_type text NOT NULL CHECK (variance_type IN ('AtReceipt','AfterReceipt')),
    total_amount_diff numeric NOT NULL,
    status text NOT NULL CHECK (status IN ('Pending','Approved','Rejected')),
    decided_by uuid,
    decided_at timestamptz,
    decision_note text CHECK (length(decision_note) <= 500),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_purchase_variances_invoice UNIQUE (invoice_id),
    CONSTRAINT ck_purchase_variances_decision CHECK (
        (status = 'Pending' AND decided_at IS NULL AND decided_by IS NULL AND decision_note IS NULL)
        OR (status IN ('Approved','Rejected') AND decided_at IS NOT NULL AND decided_by IS NOT NULL
            AND (status <> 'Rejected' OR (decision_note IS NOT NULL AND btrim(decision_note) <> '')))),
    CONSTRAINT fk_purchase_variances_tenant FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_purchase_variances_invoice FOREIGN KEY (tenant_id, invoice_id) REFERENCES public.supplier_invoices (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_purchase_variances_receipt FOREIGN KEY (tenant_id, goods_receipt_id) REFERENCES public.goods_receipts (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_purchase_variances_decider FOREIGN KEY (tenant_id, decided_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_purchase_variances_receipt
    ON public.purchase_variances (tenant_id, goods_receipt_id);

CREATE OR REPLACE FUNCTION public.oisms_supplier_invoice_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.status <> 'Draft' OR NOT EXISTS (
            SELECT 1 FROM public.goods_receipts
            WHERE tenant_id = NEW.tenant_id AND id = NEW.goods_receipt_id AND supplier_id = NEW.supplier_id
        ) THEN
            RAISE EXCEPTION 'A supplier invoice must start as Draft for its receipt supplier' USING ERRCODE = '23514';
        END IF;
        RETURN NEW;
    END IF;

    IF (NEW.tenant_id, NEW.id, NEW.goods_receipt_id, NEW.supplier_id, NEW.request_key,
        NEW.request_payload_hash, NEW.created_by, NEW.created_at) IS DISTINCT FROM
       (OLD.tenant_id, OLD.id, OLD.goods_receipt_id, OLD.supplier_id, OLD.request_key,
        OLD.request_payload_hash, OLD.created_by, OLD.created_at)
       OR NOT (
            (OLD.status = 'Draft' AND NEW.status IN ('Draft','Matched','PendingApproval','Cancelled'))
            OR (OLD.status IN ('Matched','Approved') AND NEW.status = 'Cancelled')
            OR (OLD.status = 'PendingApproval' AND NEW.status IN ('Approved','Rejected'))
       )
       OR (OLD.status <> 'Draft' AND
           (NEW.invoice_series, NEW.invoice_number, NEW.invoice_date, NEW.subtotal, NEW.vat_amount,
            NEW.shipping_fee, NEW.total_amount, NEW.submitted_at) IS DISTINCT FROM
           (OLD.invoice_series, OLD.invoice_number, OLD.invoice_date, OLD.subtotal, OLD.vat_amount,
            OLD.shipping_fee, OLD.total_amount, OLD.submitted_at)) THEN
        RAISE EXCEPTION 'Invalid supplier invoice change' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_supplier_invoice_item_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE
    parent_status text;
    parent_receipt uuid;
    parent_tenant uuid;
    parent_id uuid;
BEGIN
    IF TG_OP = 'INSERT' THEN
        parent_tenant := NEW.tenant_id;
        parent_id := NEW.invoice_id;
    ELSE
        parent_tenant := OLD.tenant_id;
        parent_id := OLD.invoice_id;
    END IF;

    SELECT status, goods_receipt_id INTO parent_status, parent_receipt
      FROM public.supplier_invoices WHERE tenant_id = parent_tenant AND id = parent_id FOR UPDATE;
    IF parent_status IS DISTINCT FROM 'Draft' THEN
        RAISE EXCEPTION 'Only draft supplier invoice items can be changed' USING ERRCODE = '23514';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    IF TG_OP = 'UPDATE' AND (NEW.tenant_id, NEW.id, NEW.invoice_id) IS DISTINCT FROM
                            (OLD.tenant_id, OLD.id, OLD.invoice_id) THEN
        RAISE EXCEPTION 'Supplier invoice item identity is immutable' USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM public.goods_receipt_items
        WHERE tenant_id = NEW.tenant_id AND id = NEW.goods_receipt_item_id
          AND receipt_id = parent_receipt AND sku_id = NEW.sku_id
    ) THEN
        RAISE EXCEPTION 'Supplier invoice item must belong to its receipt and SKU' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE OR REPLACE TRIGGER supplier_invoice_guard BEFORE INSERT OR UPDATE ON public.supplier_invoices
    FOR EACH ROW EXECUTE FUNCTION public.oisms_supplier_invoice_guard();
CREATE OR REPLACE TRIGGER supplier_invoice_no_delete BEFORE DELETE OR TRUNCATE ON public.supplier_invoices
    FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
CREATE OR REPLACE TRIGGER supplier_invoice_item_guard BEFORE INSERT OR UPDATE OR DELETE ON public.supplier_invoice_items
    FOR EACH ROW EXECUTE FUNCTION public.oisms_supplier_invoice_item_guard();
CREATE OR REPLACE TRIGGER purchase_variance_no_delete BEFORE DELETE OR TRUNCATE ON public.purchase_variances
    FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();

REVOKE ALL ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances FROM PUBLIC, oisms_runtime;
GRANT SELECT, INSERT, UPDATE ON public.supplier_invoices, public.purchase_variances TO oisms_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.supplier_invoice_items TO oisms_runtime;

COMMIT;
