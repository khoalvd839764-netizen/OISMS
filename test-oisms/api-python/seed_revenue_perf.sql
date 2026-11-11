-- Đi kèm seed_revenue_perf.py: rải ngày tạo đơn test và kiểm tra bất biến kho.
-- Chạy bằng tài khoản postgres trên DB local:
--   docker exec -i oism_postgres psql -U postgres -d oisms -v spread=1 -f - < test-oisms/api-python/seed_revenue_perf.sql
-- Bỏ "-v spread=1" để chỉ kiểm tra, không đổi dữ liệu.
--
-- Rải ngày: chỉ dời orders.created_at (cột báo cáo doanh thu lọc và gộp theo ngày) về 90 ngày gần nhất,
-- giờ bán 8h-22h giờ VN, ngày gần đây nhiều đơn hơn. confirmed_at bị trigger protect_order khóa cứng
-- và sổ cái kho append-only nên hai nơi này vẫn giữ ngày nạp thật.

\set T '11111111-1111-1111-1111-111111111111'

\if :{?spread}
begin;
select setseed(0.20261110);
update orders o set created_at = (date_trunc('day', now() at time zone 'Asia/Ho_Chi_Minh')
      - make_interval(days => floor(power(random(), 1.3) * 90)::int)
      + make_interval(hours => 8 + floor(random() * 14)::int, mins => floor(random() * 60)::int, secs => floor(random() * 60)::int)
    ) at time zone 'Asia/Ho_Chi_Minh'
where o.tenant_id = :'T' and o.customer_name like 'KH hiệu năng%';
commit;
\endif

-- 1. Số đơn test theo kênh/trạng thái và số ngày đã rải
select channel, status, count(*), sum(total_amount),
       count(distinct (created_at at time zone 'Asia/Ho_Chi_Minh')::date) as days
from orders
where tenant_id = :'T' and customer_name like 'KH hiệu năng%' group by 1, 2 order by 1, 2;

-- 2. Tồn PERF: on_hand khớp tổng ledger, bằng balance_after cuối, Available không âm
with led as (
  select sku_id,
         sum(case when type = 'IN' then quantity when type = 'OUT' then -quantity else 0 end) as net,
         (array_agg(balance_after order by sequence_no desc))[1] as last_balance,
         count(*) filter (where type = 'OUT') as outs
  from inventory_transactions where tenant_id = :'T' group by sku_id)
select s.code, ib.on_hand, ib.reserved, ib.on_hand - ib.reserved as available, led.net, led.last_balance, led.outs,
       (ib.on_hand = led.net and ib.on_hand = led.last_balance and ib.on_hand - ib.reserved >= 0) as ok
from skus s
join inventory_balances ib on ib.sku_id = s.id and ib.tenant_id = s.tenant_id
join led on led.sku_id = s.id
where s.tenant_id = :'T' and s.code like 'PERF-%' order by s.code;

-- 3. Dòng hàng thiếu bút toán OUT hoặc thiếu giá vốn snapshot (mong đợi 0)
select count(*) as items_without_out from order_items oi join orders o on o.id = oi.order_id
where o.tenant_id = :'T' and o.customer_name like 'KH hiệu năng%'
  and not exists (select 1 from inventory_transactions t where t.order_item_id = oi.id and t.type = 'OUT');
select count(*) as items_without_cost from order_items oi join orders o on o.id = oi.order_id
where o.tenant_id = :'T' and o.customer_name like 'KH hiệu năng%' and oi.cost_price is null;
