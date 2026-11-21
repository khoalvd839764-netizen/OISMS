-- Sinh dữ liệu tổng hợp 90 ngày cho tenant FORECAST-EVAL (OISMS-66-FIN-C).
-- Tái lập 100% khi chạy lại nhiều lần (xóa và tạo lại đúng tập UUID cố định).
-- CHỈ CHẠY TRÊN DB ĐÁNH GIÁ LOCAL (không chạy trên DB demo/AWS).

DO $$
DECLARE
    v_tenant_id     uuid := '99999999-0000-0000-0000-000000000001';
    v_branch_id     uuid := '99999999-0000-0000-0001-000000000001';
    v_warehouse_id  uuid := '99999999-0000-0000-0002-000000000001';
    v_supplier_id   uuid := '99999999-0000-0000-0003-000000000001';
    v_user_id       uuid := '99999999-0000-0000-0004-000000000001';
    v_product_id    uuid := '99999999-0000-0000-0005-000000000001';

    v_sku_steady1   uuid := '99999999-0000-0000-0006-000000000001';
    v_sku_steady2   uuid := '99999999-0000-0000-0006-000000000002';
    v_sku_weekly    uuid := '99999999-0000-0000-0006-000000000003';
    v_sku_intermit  uuid := '99999999-0000-0000-0006-000000000004';
    v_sku_stockout  uuid := '99999999-0000-0000-0006-000000000005';
    v_sku_nosales   uuid := '99999999-0000-0000-0006-000000000006';

    v_start_date    date := DATE '2026-08-01';
    v_day           int;
    v_cur_date      date;
    v_ts            timestamptz;

    v_seq           bigint;
    v_bal           numeric(18,3);
    v_qty           numeric(18,3);
    v_rec_id        uuid;
    v_rec_item_id   uuid;
    v_ord_id        uuid;
    v_ord_item_id   uuid;

    rec             record;
    v_skus          uuid[] := ARRAY[
        '99999999-0000-0000-0006-000000000001'::uuid,
        '99999999-0000-0000-0006-000000000002'::uuid,
        '99999999-0000-0000-0006-000000000003'::uuid,
        '99999999-0000-0000-0006-000000000004'::uuid,
        '99999999-0000-0000-0006-000000000005'::uuid,
        '99999999-0000-0000-0006-000000000006'::uuid
    ];
    v_sku           uuid;
    v_sku_idx       int;
BEGIN
    -- Đảm bảo idempotent khi chạy lại 2 lần trên DB đánh giá local (ledger có trigger append-only)
    IF EXISTS (SELECT 1 FROM tenants WHERE id = v_tenant_id) THEN
        RAISE NOTICE 'Tenant FORECAST-EVAL da ton tai, giu nguyen du lieu xac dinh.';
        RETURN;
    END IF;

    INSERT INTO tenants (id, name, is_active, created_at, updated_at)
    VALUES (v_tenant_id, 'FORECAST-EVAL', TRUE, now(), now());

    INSERT INTO users (id, tenant_id, email, password_hash, display_name, role, is_active, created_at, updated_at)
    VALUES (v_user_id, v_tenant_id, 'eval@forecast.local', 'unused', 'Forecast Eval', 'Owner', TRUE, now(), now());

    INSERT INTO branches (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES (v_branch_id, v_tenant_id, 'Chi nhanh Eval', TRUE, now(), now());

    INSERT INTO warehouses (id, tenant_id, branch_id, name, is_active, created_at, updated_at)
    VALUES (v_warehouse_id, v_tenant_id, v_branch_id, 'Kho Eval', TRUE, now(), now());

    INSERT INTO suppliers (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES (v_supplier_id, v_tenant_id, 'NCC Eval', TRUE, now(), now());

    INSERT INTO products (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES (v_product_id, v_tenant_id, 'San pham Eval', TRUE, now(), now());

    INSERT INTO skus (id, tenant_id, product_id, code, unit, retail_price, wholesale_price, is_active, created_at, updated_at)
    VALUES
        (v_sku_steady1,  v_tenant_id, v_product_id, 'EVAL-STEADY-01',       'cai', 100000, 80000, TRUE, now(), now()),
        (v_sku_steady2,  v_tenant_id, v_product_id, 'EVAL-STEADY-02',       'cai', 120000, 95000, TRUE, now(), now()),
        (v_sku_weekly,   v_tenant_id, v_product_id, 'EVAL-WEEKLY-01',       'cai', 150000, 120000, TRUE, now(), now()),
        (v_sku_intermit, v_tenant_id, v_product_id, 'EVAL-INTERMITTENT-01', 'cai', 200000, 160000, TRUE, now(), now()),
        (v_sku_stockout, v_tenant_id, v_product_id, 'EVAL-STOCKOUT-01',     'cai',  90000,  70000, TRUE, now(), now()),
        (v_sku_nosales,  v_tenant_id, v_product_id, 'EVAL-NOSALES-01',      'cai',  50000,  40000, TRUE, now(), now());

    FOR v_sku_idx IN 1..6 LOOP
        v_sku := v_skus[v_sku_idx];
        v_seq := 0;
        v_bal := 0;

        FOR v_day IN 0..89 LOOP
            v_cur_date := v_start_date + v_day;
            v_ts := (v_cur_date::timestamp + time '10:00:00') AT TIME ZONE 'Asia/Ho_Chi_Minh';

            -- Nhập kho đầu kỳ hoặc bổ sung
            v_qty := 0;
            IF v_day = 0 THEN
                v_qty := CASE v_sku_idx
                    WHEN 1 THEN 600
                    WHEN 2 THEN 500
                    WHEN 3 THEN 600
                    WHEN 4 THEN 100
                    WHEN 5 THEN 120
                    WHEN 6 THEN 100
                END;
            ELSIF v_sku_idx = 1 AND v_day = 45 THEN v_qty := 300;
            ELSIF v_sku_idx = 2 AND v_day IN (35, 70) THEN v_qty := 400;
            ELSIF v_sku_idx = 3 AND v_day = 50 THEN v_qty := 400;
            ELSIF v_sku_idx = 5 AND v_day = 38 THEN v_qty := 200;
            END IF;

            IF v_qty > 0 THEN
                v_seq := v_seq + 1;
                v_bal := v_bal + v_qty;
                v_rec_id := md5('REC-' || v_sku_idx || '-' || v_day)::uuid;
                v_rec_item_id := md5('RECI-' || v_sku_idx || '-' || v_day)::uuid;

                INSERT INTO goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at, created_at, updated_at)
                VALUES (v_rec_id, v_tenant_id, 'PN-EVAL-' || v_sku_idx || '-' || v_day, v_branch_id, v_warehouse_id, v_supplier_id, 'Confirmed', v_user_id, v_user_id, v_ts - interval '1 hour', v_ts - interval '1 hour', v_ts - interval '1 hour');

                INSERT INTO goods_receipt_items (id, tenant_id, receipt_id, sku_id, unit_name_snapshot, factor_snapshot, quantity, unit_price, created_at, updated_at)
                VALUES (v_rec_item_id, v_tenant_id, v_rec_id, v_sku, 'cai', 1, v_qty, 50000, v_ts - interval '1 hour', v_ts - interval '1 hour');

                INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, receipt_item_id, created_at)
                VALUES (md5('TX-IN-' || v_sku_idx || '-' || v_day)::uuid, v_tenant_id, v_branch_id, v_warehouse_id, v_sku, 'IN', v_qty, 50000, v_bal, v_seq, v_rec_item_id, v_ts - interval '1 hour');
            END IF;

            -- Xuất bán theo mẫu nhu cầu xác định (deterministic)
            v_qty := CASE v_sku_idx
                WHEN 1 THEN 4 + (v_day % 3)                                    -- Steady ~5/ngày
                WHEN 2 THEN 8 + ((v_day * 3) % 5)                              -- Steady ~10/ngày
                WHEN 3 THEN CASE WHEN (v_day % 7) IN (5, 6) THEN 14 ELSE 2 END -- Weekly spike cuối tuần
                WHEN 4 THEN CASE WHEN (v_day % 10) = 3 THEN 6 ELSE 0 END       -- Intermittent: 9 ngày/90 ngày
                WHEN 5 THEN LEAST(v_bal, 6)                                    -- Stockout: bán 6/ngày cho tới khi cạn
                WHEN 6 THEN 0                                                  -- NoSales
            END;

            IF v_qty > 0 THEN
                v_seq := v_seq + 1;
                v_bal := v_bal - v_qty;
                v_ord_id := md5('ORD-' || v_sku_idx || '-' || v_day)::uuid;
                v_ord_item_id := md5('ORDI-' || v_sku_idx || '-' || v_day)::uuid;

                INSERT INTO orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_by, confirmed_by, confirmed_at, completed_at, created_at, updated_at)
                VALUES (v_ord_id, v_tenant_id, 'ORD-EVAL-' || v_sku_idx || '-' || v_day, v_branch_id, 'POS', 'Completed', v_qty * 100000, v_ord_id, v_user_id, v_user_id, v_ts, v_ts, v_ts, v_ts);

                INSERT INTO order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_name_snapshot, factor_snapshot, quantity, unit_price, cost_price, created_at, updated_at)
                VALUES (v_ord_item_id, v_tenant_id, v_ord_id, v_sku, 'EVAL-' || v_sku_idx, 'Eval SKU', 'cai', 1, v_qty, 100000, 50000, v_ts, v_ts);

                INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, order_item_id, created_at)
                VALUES (md5('TX-OUT-' || v_sku_idx || '-' || v_day)::uuid, v_tenant_id, v_branch_id, v_warehouse_id, v_sku, 'OUT', v_qty, 50000, v_bal, v_seq, v_ord_item_id, v_ts);
            END IF;
        END LOOP;

        INSERT INTO warehouse_inventory_balances (id, tenant_id, branch_id, warehouse_id, sku_id, on_hand, reserved, last_sequence, created_at, updated_at)
        VALUES (
            md5('WBAL-' || v_sku_idx)::uuid,
            v_tenant_id,
            v_branch_id,
            v_warehouse_id,
            v_sku,
            v_bal,
            0,
            v_seq,
            now(),
            now()
        );

        INSERT INTO inventory_balances (id, tenant_id, branch_id, sku_id, on_hand, reserved, average_cost, reorder_point, last_sequence, created_at, updated_at)
        VALUES (
            md5('BAL-' || v_sku_idx)::uuid,
            v_tenant_id,
            v_branch_id,
            v_sku,
            v_bal,
            0,
            50000,
            CASE v_sku_idx WHEN 1 THEN 30 WHEN 2 THEN 180 WHEN 3 THEN 40 WHEN 4 THEN 15 WHEN 5 THEN 20 ELSE 10 END,
            v_seq,
            now(),
            now()
        );
    END LOOP;
END $$;

-- FIN-C-fix1 (21/11/2026): bổ sung 2 SKU bán đều có tồn thấp ở một số mốc t để nhóm Eligible
-- có ca "thực sự cần nhập" (đo được recall) mà vẫn không hết hàng (nhập bù khi tồn < ngưỡng).
-- Khối riêng, idempotent theo SKU: chạy lại không chèn trùng, DB đã có 6 SKU cũ không phải tạo lại.
DO $$
DECLARE
    v_tenant_id     uuid := '99999999-0000-0000-0000-000000000001';
    v_branch_id     uuid := '99999999-0000-0000-0001-000000000001';
    v_warehouse_id  uuid := '99999999-0000-0000-0002-000000000001';
    v_supplier_id   uuid := '99999999-0000-0000-0003-000000000001';
    v_user_id       uuid := '99999999-0000-0000-0004-000000000001';
    v_product_id    uuid := '99999999-0000-0000-0005-000000000001';
    v_start_date    date := DATE '2026-08-01';
    v_sku           uuid;
    v_sku_idx       int;
    v_day           int;
    v_cur_date      date;
    v_ts            timestamptz;
    v_seq           bigint;
    v_bal           numeric(18,3);
    v_qty           numeric(18,3);
    v_rec_id        uuid;
    v_rec_item_id   uuid;
    v_ord_id        uuid;
    v_ord_item_id   uuid;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tenants WHERE id = v_tenant_id) THEN
        RAISE EXCEPTION 'Chạy khối đầu tiên của seed-eval.sql trước (chưa có tenant FORECAST-EVAL).';
    END IF;

    FOR v_sku_idx IN 7..8 LOOP
        v_sku := ('99999999-0000-0000-0006-00000000000' || v_sku_idx)::uuid;
        IF EXISTS (SELECT 1 FROM skus WHERE id = v_sku) THEN
            CONTINUE;
        END IF;

        INSERT INTO skus (id, tenant_id, product_id, code, unit, retail_price, wholesale_price, is_active, created_at, updated_at)
        VALUES (v_sku, v_tenant_id, v_product_id,
                CASE v_sku_idx WHEN 7 THEN 'EVAL-LOWSTOCK-STEADY-01' ELSE 'EVAL-LOWSTOCK-WEEKLY-01' END,
                'cai', 100000, 80000, TRUE, now(), now());

        v_seq := 0;
        v_bal := 0;

        FOR v_day IN 0..89 LOOP
            v_cur_date := v_start_date + v_day;
            v_ts := (v_cur_date::timestamp + time '10:00:00') AT TIME ZONE 'Asia/Ho_Chi_Minh';

            -- Nhập đầu kỳ và nhập bù khi tồn xuống dưới ngưỡng, để tồn dao động thấp nhưng không về 0
            v_qty := 0;
            IF v_day = 0 THEN
                v_qty := CASE v_sku_idx WHEN 7 THEN 150 ELSE 120 END;
            ELSIF v_sku_idx = 7 AND v_bal < 40 THEN
                v_qty := 150;
            ELSIF v_sku_idx = 8 AND v_bal < 35 THEN
                v_qty := 100;
            END IF;

            IF v_qty > 0 THEN
                v_seq := v_seq + 1;
                v_bal := v_bal + v_qty;
                v_rec_id := md5('REC-' || v_sku_idx || '-' || v_day)::uuid;
                v_rec_item_id := md5('RECI-' || v_sku_idx || '-' || v_day)::uuid;

                INSERT INTO goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at, created_at, updated_at)
                VALUES (v_rec_id, v_tenant_id, 'PN-EVAL-' || v_sku_idx || '-' || v_day, v_branch_id, v_warehouse_id, v_supplier_id, 'Confirmed', v_user_id, v_user_id, v_ts - interval '1 hour', v_ts - interval '1 hour', v_ts - interval '1 hour');

                INSERT INTO goods_receipt_items (id, tenant_id, receipt_id, sku_id, unit_name_snapshot, factor_snapshot, quantity, unit_price, created_at, updated_at)
                VALUES (v_rec_item_id, v_tenant_id, v_rec_id, v_sku, 'cai', 1, v_qty, 50000, v_ts - interval '1 hour', v_ts - interval '1 hour');

                INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, receipt_item_id, created_at)
                VALUES (md5('TX-IN-' || v_sku_idx || '-' || v_day)::uuid, v_tenant_id, v_branch_id, v_warehouse_id, v_sku, 'IN', v_qty, 50000, v_bal, v_seq, v_rec_item_id, v_ts - interval '1 hour');
            END IF;

            v_qty := CASE v_sku_idx
                WHEN 7 THEN 9 + (v_day % 3)                                     -- Bán đều ~10/ngày
                ELSE CASE WHEN (v_day % 7) IN (5, 6) THEN 15 ELSE 3 END         -- Theo tuần, ~6,4/ngày
            END;

            v_seq := v_seq + 1;
            v_bal := v_bal - v_qty;
            v_ord_id := md5('ORD-' || v_sku_idx || '-' || v_day)::uuid;
            v_ord_item_id := md5('ORDI-' || v_sku_idx || '-' || v_day)::uuid;

            INSERT INTO orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_by, confirmed_by, confirmed_at, completed_at, created_at, updated_at)
            VALUES (v_ord_id, v_tenant_id, 'ORD-EVAL-' || v_sku_idx || '-' || v_day, v_branch_id, 'POS', 'Completed', v_qty * 100000, v_ord_id, v_user_id, v_user_id, v_ts, v_ts, v_ts, v_ts);

            INSERT INTO order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_name_snapshot, factor_snapshot, quantity, unit_price, cost_price, created_at, updated_at)
            VALUES (v_ord_item_id, v_tenant_id, v_ord_id, v_sku, 'EVAL-' || v_sku_idx, 'Eval SKU', 'cai', 1, v_qty, 100000, 50000, v_ts, v_ts);

            INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, order_item_id, created_at)
            VALUES (md5('TX-OUT-' || v_sku_idx || '-' || v_day)::uuid, v_tenant_id, v_branch_id, v_warehouse_id, v_sku, 'OUT', v_qty, 50000, v_bal, v_seq, v_ord_item_id, v_ts);
        END LOOP;

        INSERT INTO warehouse_inventory_balances (id, tenant_id, branch_id, warehouse_id, sku_id, on_hand, reserved, last_sequence, created_at, updated_at)
        VALUES (md5('WBAL-' || v_sku_idx)::uuid, v_tenant_id, v_branch_id, v_warehouse_id, v_sku, v_bal, 0, v_seq, now(), now());

        INSERT INTO inventory_balances (id, tenant_id, branch_id, sku_id, on_hand, reserved, average_cost, reorder_point, last_sequence, created_at, updated_at)
        VALUES (md5('BAL-' || v_sku_idx)::uuid, v_tenant_id, v_branch_id, v_sku, v_bal, 0, 50000,
                CASE v_sku_idx WHEN 7 THEN 30 ELSE 20 END, v_seq, now(), now());
    END LOOP;
END $$;
