#!/usr/bin/env python3
"""
Bắn đơn giả vào tenant demo A (owner@shopa.local) qua API thật để đo tốc độ báo cáo doanh thu.

Luồng: tạo 20 sản phẩm PERF-01..20 (có giá bán lẻ ban đầu) -> phiếu mua hàng -> duyệt
-> phiếu nhập -> xác nhận (WAC, ledger IN) -> N_POS đơn POS checkout (Completed, ledger OUT)
+ N_MKT đơn sàn Shopee/Lazada qua webhook bằng JWT Owner (Reserved -> confirm -> complete).
Mọi đơn có customer_name bắt đầu bằng 'KH hiệu năng' để rải ngày và kiểm tra bằng
seed_revenue_perf.sql.

Yêu cầu: Python 3.9+, `requests`. Backend chạy với dữ liệu seed demo (Docker local).

Cách chạy (mặc định 3500 POS + 1500 sàn, khoảng 16 phút trên máy dev):
    export OISMS_PASSWORD='<mật khẩu tài khoản demo>'
    N_POS=3 N_MKT=2 python3 test-oisms/api-python/seed_revenue_perf.py          # chạy thử, có nhập kho
    N_POS=3497 N_MKT=1498 python3 test-oisms/api-python/seed_revenue_perf.py --skip-stock
    docker exec -i oism_postgres psql -U postgres -d oisms -v spread=1 -f - < test-oisms/api-python/seed_revenue_perf.sql

Biến môi trường: OISMS_API (mặc định http://localhost:5000), OISMS_PASSWORD, N_POS, N_MKT.

CẢNH BÁO: script GHI dữ liệu thật và sổ cái kho (inventory_transactions) chặn UPDATE/DELETE,
nên dữ liệu không gỡ được. Script từ chối chạy khi OISMS_API không phải localhost/127.0.0.1.
Không chạy trên bản demo AWS. Script tạo thêm kết nối sàn PERF_SHOPEE và PERF_LAZADA.
Mỗi lần chạy không --skip-stock sẽ nhập thêm 1500 cái mỗi SKU.
"""
import os
import random
import sys
import time
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from urllib.parse import urlparse

import requests

BASE = os.environ.get("OISMS_API", "http://localhost:5000").rstrip("/")
if urlparse(BASE).hostname not in ("localhost", "127.0.0.1", "::1"):
    sys.exit(f"Từ chối chạy: {BASE} không phải backend local. Dữ liệu và sổ cái kho không gỡ được.")

EMAIL = "owner@shopa.local"
PASSWORD = os.environ.get("OISMS_PASSWORD") or sys.exit("Thiếu biến môi trường OISMS_PASSWORD.")
# Id cố định của dữ liệu seed demo (OismsDatabaseSeeder)
BRANCH = "11111111-0000-0000-0001-000000000001"
WAREHOUSE = "11111111-0000-0000-0002-000000000001"
SUPPLIER = "11111111-0000-0000-0007-000000000001"
UNIT_PIECE = "11111111-0000-0000-0004-000000000001"
N_SKU = 20
N_POS = int(os.environ.get("N_POS", 3500))
N_MKT = int(os.environ.get("N_MKT", 1500))
STOCK_PER_SKU = 1500
# Các đơn tranh 20 SKU nên tăng số luồng chỉ làm request chờ khóa nhau
WORKERS = 6
random.seed(20261110)

s = requests.Session()


def login():
    r = s.post(f"{BASE}/api/auth/login", json={"email": EMAIL, "password": PASSWORD})
    r.raise_for_status()
    s.headers["Authorization"] = f"Bearer {r.json()['accessToken']}"


def call(method, path, body=None, key=None, ok=(200, 201), retries=4):
    """Retry khi lỗi mạng/5xx với CÙNG Idempotency-Key để không tạo trùng."""
    headers = {"Idempotency-Key": key} if key else {}
    for attempt in range(retries):
        try:
            r = s.request(method, f"{BASE}{path}", json=body, headers=headers, timeout=60)
        except requests.RequestException:
            if attempt == retries - 1:
                raise
            time.sleep(1 + attempt)
            continue
        if r.status_code == 401 and attempt < retries - 1:
            login()
            continue
        if r.status_code in ok:
            return r.json() if r.text else None
        if r.status_code >= 500 and attempt < retries - 1:
            time.sleep(1 + attempt)
            continue
        raise RuntimeError(f"{method} {path} -> {r.status_code}: {r.text[:400]}")


NAMES = ["Áo thun", "Áo polo", "Quần jean", "Quần kaki", "Váy liền", "Áo sơ mi", "Áo khoác", "Quần short",
         "Đầm dạ hội", "Áo len", "Mũ lưỡi trai", "Thắt lưng", "Ví da", "Túi xách", "Giày sneaker",
         "Dép quai hậu", "Tất cổ cao", "Khăn choàng", "Áo hoodie", "Quần jogger"]


def ensure_products():
    existing = call("GET", "/api/products?search=PERF-")
    have = {}
    for p in existing or []:
        for v in p.get("variants", []):
            if v.get("sku", "").startswith("PERF-"):
                have[v["sku"]] = v
    skus = []
    for i in range(N_SKU):
        code = f"PERF-{i + 1:02d}"
        price = random.choice([59000, 89000, 129000, 159000, 199000, 249000, 299000, 399000, 499000])
        if code not in have:
            call("POST", "/api/products", {
                "name": f"{NAMES[i]} (test hiệu năng)",
                "variants": [{"sku": code, "variantName": "Mặc định", "retailPrice": price,
                              "wholesalePrice": round(price * 0.85), "baseUnitId": UNIT_PIECE}],
            })
            found = call("GET", f"/api/products?search={code}")
            have[code] = next(v for p in found for v in p["variants"] if v["sku"] == code)
        v = have[code]
        retail = v.get("retailPrice") or price
        skus.append({"id": v["id"], "code": code, "price": float(retail)})
    return skus


def stock_in(skus):
    po = call("POST", "/api/purchase-orders", {
        "supplierId": SUPPLIER, "branchId": BRANCH, "note": "Nhập kho cho test hiệu năng báo cáo",
        "items": [{"skuId": k["id"], "unitId": UNIT_PIECE, "quantity": STOCK_PER_SKU,
                   "unitPrice": round(k["price"] * random.uniform(0.45, 0.65))} for k in skus],
    }, key=str(uuid.uuid4()))
    po = call("POST", f"/api/purchase-orders/{po['id']}/approve", {"confirmZeroPrice": False})
    gr = call("POST", "/api/goods-receipts", {
        "purchaseOrderId": po["id"], "warehouseId": WAREHOUSE, "note": "Test hiệu năng",
        "items": [{"purchaseOrderItemId": it["id"], "quantity": STOCK_PER_SKU} for it in po["items"]],
    }, key=str(uuid.uuid4()))
    call("POST", f"/api/goods-receipts/{gr['id']}/confirm")


def pick_lines(skus):
    lines = random.sample(skus, random.choice([1, 1, 1, 2, 2, 3]))
    return [(k, random.choice([1, 1, 1, 2, 2, 3])) for k in lines]


def pos_order(n, skus):
    lines = pick_lines(skus)
    total = sum(k["price"] * q for k, q in lines)
    key = str(uuid.uuid4())
    method = random.choice(["Cash", "Cash", "BankTransfer"])
    body = {
        "branchId": BRANCH, "channel": "POS", "customerName": f"KH hiệu năng {n}",
        "paymentMethod": method, "requestKey": key, "totalAmount": total,
        "items": [{"skuId": k["id"], "skuCode": k["code"], "unitId": UNIT_PIECE, "quantity": q,
                   "unitPrice": k["price"]} for k, q in lines],
    }
    if method == "Cash":
        body["amountGiven"] = total
        body["changeDue"] = 0
    call("POST", "/api/pos/checkout", body, key=key)


def mkt_order(n, skus, shops):
    channel, shop = random.choice(shops)
    lines = pick_lines(skus)
    # Giá sàn dao động quanh giá niêm yết
    priced = [(k, q, round(k["price"] * random.uniform(0.9, 1.0))) for k, q in lines]
    payload = {
        "channel": channel, "externalShopId": shop,
        "externalOrderId": f"PERF-{channel[:2].upper()}-{uuid.uuid4().hex[:12]}",
        "customerName": f"KH hiệu năng {n}", "customerPhone": "0900000000",
        "shippingAddress": "Địa chỉ test hiệu năng",
        "totalAmount": sum(p * q for _, q, p in priced),
        "orderTimestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "items": [{"skuCode": k["code"], "quantity": q, "unitPrice": p} for k, q, p in priced],
    }
    res = call("POST", f"/api/webhooks/simulator/{channel.lower()}", payload)
    oid = res["orderId"]
    call("POST", f"/api/orders/{oid}/confirm", {"note": "test hiệu năng"})
    call("POST", f"/api/orders/{oid}/complete", {"note": "test hiệu năng"})


def ensure_shops():
    shops = []
    current = call("GET", "/api/shop-integrations")
    for channel, shop in [("Shopee", "PERF_SHOPEE"), ("Lazada", "PERF_LAZADA")]:
        if not any(i["channel"] == channel and i["externalShopId"] == shop for i in current):
            call("POST", "/api/shop-integrations", {"channel": channel, "externalShopId": shop, "branchId": BRANCH})
        shops.append((channel, shop))
    return shops


def run_batch(label, fn, count):
    started = time.time()
    done = failed = 0
    with ThreadPoolExecutor(WORKERS) as ex:
        futures = [ex.submit(fn, i) for i in range(count)]
        for f in as_completed(futures):
            try:
                f.result()
                done += 1
            except Exception as e:  # noqa: BLE001
                failed += 1
                if failed <= 5:
                    print(f"  lỗi {label}: {e}", flush=True)
            if (done + failed) % 500 == 0:
                print(f"  {label}: {done + failed}/{count} ({time.time() - started:.0f}s)", flush=True)
    print(f"{label}: OK {done}, lỗi {failed}, {time.time() - started:.0f}s", flush=True)
    return done, failed


if __name__ == "__main__":
    login()
    skus = ensure_products()
    print("SKU:", ", ".join(f"{k['code']}={k['price']:.0f}" for k in skus), flush=True)
    if "--skip-stock" not in sys.argv:
        stock_in(skus)
        print(f"Đã nhập {STOCK_PER_SKU} mỗi SKU", flush=True)
    shops = ensure_shops()
    base = int(time.time())
    _, pos_failed = run_batch("POS", lambda i: pos_order(f"P{base}-{i}", skus), N_POS)
    _, mkt_failed = run_batch("Sàn", lambda i: mkt_order(f"M{base}-{i}", skus, shops), N_MKT)
    sys.exit(1 if pos_failed or mkt_failed else 0)
