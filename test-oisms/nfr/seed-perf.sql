-- OISMS-66-FIN-D (Q7): Sinh ~100.000 dòng order_items + 100.000 dòng ledger OUT cho tenant PERF trên DB đo riêng (oisms_perf).
-- Dữ liệu được chèn trực tiếp bằng SQL bulk (không đi qua API) để phục vụ đo hiệu năng đọc và index.
-- KHÔNG chạy trên DB làm việc hằng ngày (oisms) hay AWS.

DO $$
DECLARE
    v_tenant_id    uuid := '88888888-0000-0000-0000-000000000001';
    v_branch_id    uuid := '88888888-0000-0000-0001-000000000001';
    v_warehouse_id uuid := '88888888-0000-0000-0002-000000000001';
    v_user_id      uuid := '88888888-0000-0000-0004-000000000001';
    v_product_id   uuid := '88888888-0000-0000-0005-000000000001';
    v_pass_hash    text;
    v_sku_id       uuid;
    v_i            int;
BEGIN
    IF current_database() IN ('oisms', 'postgres', 'template0', 'template1') THEN
        RAISE EXCEPTION 'Từ chối chạy seed-perf.sql trên database hệ thống/làm việc: %', current_database();
    END IF;

    IF EXISTS (SELECT 1 FROM tenants WHERE id = v_tenant_id) THEN
        RAISE NOTICE 'Tenant PERF da ton tai trong %, bo qua seed lai.', current_database();
        RETURN;
    END IF;

    -- Lấy bcrypt hash chuẩn của mật khẩu demo nếu có, hoặc dùng hash BCrypt hợp lệ của 'YourStrongDemoPassword123!'
    v_pass_hash := '$2a$11$K2iW8dQ8f7G6h5J4k3L2mO9pQrStUvWxYzAbCdEfGhIjKlMnOpQrS';

    INSERT INTO tenants (id, name, is_active, created_at, updated_at)
    VALUES (v_tenant_id, 'PERF', TRUE, now(), now());

    INSERT INTO users (id, tenant_id, email, password_hash, display_name, role, is_active, created_at, updated_at)
    VALUES (v_user_id, v_tenant_id, 'perf@oisms.local', v_pass_hash, 'Perf Owner', 'Owner', TRUE, now(), now());

    INSERT INTO branches (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES (v_branch_id, v_tenant_id, 'Chi nhanh PERF', TRUE, now(), now());

    INSERT INTO warehouses (id, tenant_id, branch_id, name, is_active, created_at, updated_at)
    VALUES (v_warehouse_id, v_tenant_id, v_branch_id, 'Kho PERF', TRUE, now(), now());

    INSERT INTO products (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES (v_product_id, v_tenant_id, 'San pham PERF', TRUE, now(), now());

    -- Cấu hình đơn vị gốc + bảng giá retail để POS checkout và Admin order hoạt động
    INSERT INTO units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active, created_at, updated_at)
    VALUES ('88888888-0000-0000-0010-000000000001', v_tenant_id, 'cai', 'Cái', 'cái', 'Count', 0, TRUE, now(), now());

    INSERT INTO price_lists (id, tenant_id, code, name, is_active, created_at, updated_at)
    VALUES ('88888888-0000-0000-0011-000000000001', v_tenant_id, 'retail', 'Giá bán lẻ', TRUE, now(), now());

    -- Tạo 25 SKU (đủ test lọc 1 SKU và 20 SKU)
    FOR v_i IN 1..25 LOOP
        v_sku_id := ('88888888-0000-0000-0006-' || lpad(v_i::text, 12, '0'))::uuid;
        INSERT INTO skus (id, tenant_id, product_id, code, barcode, barcode_type, unit, base_unit_id, retail_price, wholesale_price, is_active, created_at, updated_at)
        VALUES (
            v_sku_id, v_tenant_id, v_product_id,
            'PERF-SKU-' || lpad(v_i::text, 2, '0'),
            'PERF-BC-' || lpad(v_i::text, 2, '0'),
            'Code128',
            'cai', '88888888-0000-0000-0010-000000000001', 100000, 80000, TRUE, now(), now()
        );
    END LOOP;

    INSERT INTO sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_purchase, can_sell, is_default_purchase, is_default_sale, quantity_decimal_places)
    SELECT
        ('88888888-0000-0000-0014-' || lpad(s_i::text, 12, '0'))::uuid,
        v_tenant_id,
        ('88888888-0000-0000-0006-' || lpad(s_i::text, 12, '0'))::uuid,
        '88888888-0000-0000-0010-000000000001',
        1, TRUE, TRUE, TRUE, TRUE, 0
    FROM generate_series(1, 25) AS s_i;

    INSERT INTO price_change_requests (id, tenant_id, request_key, kind, status, reason_type, reason, created_by, created_at, reviewed_by, reviewed_at)
    VALUES ('88888888-0000-0000-0015-000000000001', v_tenant_id, '88888888-0000-0000-0015-000000000001', 'Opening', 'Approved', 'OTHER', 'Perf seed', v_user_id, now() - interval '35 days', v_user_id, now() - interval '35 days');

    INSERT INTO price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
    SELECT
        ('88888888-0000-0000-0013-' || lpad(s_i::text, 12, '0'))::uuid,
        v_tenant_id,
        '88888888-0000-0000-0015-000000000001',
        '88888888-0000-0000-0011-000000000001',
        ('88888888-0000-0000-0006-' || lpad(s_i::text, 12, '0'))::uuid,
        '88888888-0000-0000-0010-000000000001',
        100000,
        now() - interval '35 days'
    FROM generate_series(1, 25) AS s_i;

    INSERT INTO sku_unit_prices (id, tenant_id, sku_id, unit_id, price_list_id, price, valid_from, source_item_id)
    SELECT
        ('88888888-0000-0000-0012-' || lpad(s_i::text, 12, '0'))::uuid,
        v_tenant_id,
        ('88888888-0000-0000-0006-' || lpad(s_i::text, 12, '0'))::uuid,
        '88888888-0000-0000-0010-000000000001',
        '88888888-0000-0000-0011-000000000001',
        100000,
        now() - interval '35 days',
        ('88888888-0000-0000-0013-' || lpad(s_i::text, 12, '0'))::uuid
    FROM generate_series(1, 25) AS s_i;

    -- Bulk 50.000 đơn hàng (trong 30 ngày gần nhất)
    INSERT INTO orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_by, confirmed_by, confirmed_at, completed_at, created_at, updated_at)
    SELECT
        md5('PERF-ORD-' || g)::uuid,
        v_tenant_id,
        'ORD-PERF-' || lpad(g::text, 6, '0'),
        v_branch_id,
        CASE (g % 2) WHEN 0 THEN 'POS' ELSE 'Admin' END,
        'Completed',
        200000,
        md5('PERF-REQ-' || g)::uuid,
        v_user_id,
        v_user_id,
        now() - ((g % 30) || ' days')::interval,
        now() - ((g % 30) || ' days')::interval,
        now() - ((g % 30) || ' days')::interval,
        now() - ((g % 30) || ' days')::interval
    FROM generate_series(1, 50000) AS g;

    -- Bulk 100.000 dòng order_items (2 dòng / đơn)
    INSERT INTO order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_name_snapshot, factor_snapshot, quantity, unit_price, cost_price, created_at, updated_at)
    SELECT
        md5('PERF-OI-' || g || '-' || k)::uuid,
        v_tenant_id,
        md5('PERF-ORD-' || g)::uuid,
        ('88888888-0000-0000-0006-' || lpad((((g + k) % 25) + 1)::text, 12, '0'))::uuid,
        'PERF-SKU-' || lpad((((g + k) % 25) + 1)::text, 2, '0'),
        'San pham PERF',
        'cai',
        1,
        1,
        100000,
        60000,
        now() - ((g % 30) || ' days')::interval,
        now() - ((g % 30) || ' days')::interval
    FROM generate_series(1, 50000) AS g
    CROSS JOIN (VALUES (1), (2)) AS t(k);

    INSERT INTO suppliers (id, tenant_id, name, is_active, created_at, updated_at)
    VALUES ('88888888-0000-0000-0003-000000000001', v_tenant_id, 'NCC PERF', TRUE, now(), now());

    -- Nhập kho đầu kỳ 100.000/SKU (sequence_no = 1) để tổng IN - OUT khớp tuyệt đối với trigger oisms_check_stock
    INSERT INTO goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at, created_at, updated_at)
    VALUES (
        '88888888-0000-0000-0007-000000000001',
        v_tenant_id,
        'PN-PERF-INIT',
        v_branch_id,
        v_warehouse_id,
        '88888888-0000-0000-0003-000000000001',
        'Confirmed',
        v_user_id,
        v_user_id,
        now() - interval '35 days',
        now() - interval '35 days',
        now() - interval '35 days'
    );

    INSERT INTO goods_receipt_items (id, tenant_id, receipt_id, sku_id, unit_name_snapshot, factor_snapshot, quantity, unit_price, created_at, updated_at)
    SELECT
        ('88888888-0000-0000-0008-' || lpad(s_i::text, 12, '0'))::uuid,
        v_tenant_id,
        '88888888-0000-0000-0007-000000000001',
        ('88888888-0000-0000-0006-' || lpad(s_i::text, 12, '0'))::uuid,
        'cai', 1, 100000, 60000, now() - interval '35 days', now() - interval '35 days'
    FROM generate_series(1, 25) AS s_i;

    INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, receipt_item_id, created_at)
    SELECT
        ('88888888-0000-0000-0009-' || lpad(s_i::text, 12, '0'))::uuid,
        v_tenant_id,
        v_branch_id,
        v_warehouse_id,
        ('88888888-0000-0000-0006-' || lpad(s_i::text, 12, '0'))::uuid,
        'IN', 100000, 60000, 100000, 1,
        ('88888888-0000-0000-0008-' || lpad(s_i::text, 12, '0'))::uuid,
        now() - interval '35 days'
    FROM generate_series(1, 25) AS s_i;

    -- Bulk 100.000 dòng inventory_transactions OUT tương ứng (sequence_no bắt đầu từ 2 theo từng SKU)
    INSERT INTO inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, order_item_id, created_at)
    SELECT
        md5('PERF-TX-' || oi.id::text)::uuid,
        v_tenant_id,
        v_branch_id,
        v_warehouse_id,
        oi.sku_id,
        'OUT',
        1,
        60000,
        100000 - row_number() OVER (PARTITION BY oi.sku_id ORDER BY oi.created_at, oi.id),
        1 + row_number() OVER (PARTITION BY oi.sku_id ORDER BY oi.created_at, oi.id),
        oi.id,
        oi.created_at
    FROM order_items oi
    WHERE oi.tenant_id = v_tenant_id;

    -- Cập nhật tồn kho cuối cùng khớp với ledger (100.000 - số lượng OUT)
    INSERT INTO warehouse_inventory_balances (id, tenant_id, branch_id, warehouse_id, sku_id, on_hand, reserved, last_sequence, created_at, updated_at)
    SELECT
        md5('PERF-WBAL-' || it.sku_id::text)::uuid,
        v_tenant_id,
        v_branch_id,
        v_warehouse_id,
        it.sku_id,
        100000 - count(*) FILTER (WHERE it.type = 'OUT'),
        0,
        max(it.sequence_no),
        now(),
        now()
    FROM inventory_transactions it
    WHERE it.tenant_id = v_tenant_id
    GROUP BY it.sku_id;

    INSERT INTO inventory_balances (id, tenant_id, branch_id, sku_id, on_hand, reserved, average_cost, reorder_point, last_sequence, created_at, updated_at)
    SELECT
        md5('PERF-BAL-' || it.sku_id::text)::uuid,
        v_tenant_id,
        v_branch_id,
        it.sku_id,
        100000 - count(*) FILTER (WHERE it.type = 'OUT'),
        0,
        60000,
        100,
        max(it.sequence_no),
        now(),
        now()
    FROM inventory_transactions it
    WHERE it.tenant_id = v_tenant_id
    GROUP BY it.sku_id;

    ANALYZE orders;
    ANALYZE order_items;
    ANALYZE inventory_transactions;
    ANALYZE inventory_balances;
    ANALYZE skus;
END $$;
