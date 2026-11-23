-- NFR-TENANT-02: Kích thước bảng và EXPLAIN (ANALYZE, BUFFERS) trên DB đo (oisms_perf)
SELECT
    relname AS table_name,
    n_live_tup AS approx_rows,
    pg_size_pretty(pg_total_relation_size(relid)) AS total_size
FROM pg_stat_user_tables
WHERE relname IN ('orders', 'order_items', 'inventory_transactions', 'inventory_balances', 'skus')
ORDER BY pg_total_relation_size(relid) DESC;

-- 1. Ledger theo SKU / chi nhánh
EXPLAIN (ANALYZE, BUFFERS)
SELECT id, type, quantity, balance_after, sequence_no, created_at
FROM inventory_transactions
WHERE tenant_id = '88888888-0000-0000-0000-000000000001'
  AND branch_id = '88888888-0000-0000-0001-000000000001'
  AND sku_id = '88888888-0000-0000-0006-000000000001'
  AND created_at >= now() - interval '30 days'
ORDER BY created_at DESC, id DESC
LIMIT 50;

-- 2. Danh sách đơn theo tenant + ngày
EXPLAIN (ANALYZE, BUFFERS)
SELECT id, number, channel, status, total_amount, created_at
FROM orders
WHERE tenant_id = '88888888-0000-0000-0000-000000000001'
  AND created_at >= now() - interval '7 days'
ORDER BY created_at DESC
LIMIT 50;

-- 3. Báo cáo doanh thu lọc SKU (SQL tương đương EF Core EXISTS)
EXPLAIN (ANALYZE, BUFFERS)
SELECT o.id, o.branch_id, o.channel, o.total_amount, o.created_at
FROM orders o
WHERE o.tenant_id = '88888888-0000-0000-0000-000000000001'
  AND o.status IN ('Confirmed', 'Completed')
  AND o.created_at >= now() - interval '30 days'
  AND EXISTS (
      SELECT 1
      FROM order_items oi
      WHERE oi.tenant_id = '88888888-0000-0000-0000-000000000001'
        AND oi.order_id = o.id
        AND oi.sku_id = ANY (ARRAY['88888888-0000-0000-0006-000000000001'::uuid])
  );
