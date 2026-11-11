#!/usr/bin/env python3
"""
Script kiểm thử API chức năng TRẢ HÀNG TỪ KHÁCH (CS-01) của OISMS.

Kiểm thử hộp đen qua HTTP trên backend đang chạy (mặc định http://localhost:5000).
Script tự tạo dữ liệu: bán 1 đơn POS mới 3 sản phẩm, rồi chạy các ca trả hàng
trên chính đơn đó, nên chạy lại nhiều lần không ảnh hưởng nhau.

Yêu cầu: Python 3.9+, thư viện `requests` (pip install requests).

Cách chạy:
    export OISMS_PASSWORD='<mật khẩu tài khoản demo>'
    python3 test-oisms/api-python/test_sales_return_api.py
    python3 test-oisms/api-python/test_sales_return_api.py --base-url http://localhost:5000 -v

Biến môi trường / tham số:
    OISMS_BASE_URL  / --base-url   Địa chỉ backend (không có đuôi /api)
    OISMS_PASSWORD  / --password   Mật khẩu chung của các tài khoản demo
    --sku-query                    Từ khóa tìm SKU để bán (mặc định POLO)

Lưu ý: script GHI dữ liệu thật (đơn POS, phiếu trả, sổ kho). Chỉ chạy trên môi
trường local/test, không chạy trên bản demo production.

Mã thoát: 0 nếu mọi ca PASS, 1 nếu có ca FAIL, 2 nếu không chuẩn bị được dữ liệu.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import time
import traceback
import uuid
from dataclasses import dataclass, field
from decimal import Decimal

try:
    import requests
except ImportError:  # pragma: no cover
    print("Thiếu thư viện requests. Cài bằng: pip install requests")
    sys.exit(2)


# ---------------------------------------------------------------------------
# Hạ tầng gọi API và ghi kết quả
# ---------------------------------------------------------------------------

ACCOUNTS = {
    "owner": "owner@shopa.local",
    "staff": "staff@shopa.local",
    "cashier": "cashier@shopa.local",
    "owner_b": "owner@shopb.local",  # tenant khác, dùng kiểm tra cô lập dữ liệu
}


class Api:
    def __init__(self, base_url: str, token: str | None = None, verbose: bool = False):
        self.base_url = base_url.rstrip("/")
        self.token = token
        self.verbose = verbose

    def request(self, method: str, path: str, *, json=None, params=None, headers=None):
        h = {"Accept": "application/json"}
        if self.token:
            h["Authorization"] = f"Bearer {self.token}"
        if headers:
            h.update(headers)
        resp = requests.request(method, self.base_url + path, json=json, params=params, headers=h, timeout=30)
        if self.verbose:
            print(f"      {method} {path} -> {resp.status_code}")
        return resp

    def get(self, path, **kw):
        return self.request("GET", path, **kw)

    def post(self, path, **kw):
        return self.request("POST", path, **kw)


def login(base_url: str, email: str, password: str, verbose: bool) -> Api:
    resp = requests.post(
        base_url.rstrip("/") + "/api/auth/login",
        json={"email": email, "password": password},
        timeout=30,
    )
    if resp.status_code != 200:
        raise RuntimeError(f"Đăng nhập {email} thất bại: HTTP {resp.status_code} {resp.text[:200]}")
    return Api(base_url, resp.json()["accessToken"], verbose)


def error_code(resp) -> str | None:
    """Lấy errorCode từ ProblemDetails (RFC 7807)."""
    try:
        body = resp.json()
    except ValueError:
        return None
    if not isinstance(body, dict):
        return None
    return body.get("errorCode") or (body.get("extensions") or {}).get("errorCode")


def dec(value) -> Decimal:
    return Decimal(str(value))


class CheckFailed(AssertionError):
    pass


def check(cond: bool, message: str):
    if not cond:
        raise CheckFailed(message)


def expect_status(resp, status: int, code: str | None = None):
    detail = resp.text[:300]
    check(resp.status_code == status, f"mong đợi HTTP {status}, nhận {resp.status_code}: {detail}")
    if code is not None:
        actual = error_code(resp)
        check(actual == code, f"mong đợi errorCode {code}, nhận {actual}: {detail}")


@dataclass
class Result:
    tc_id: str
    title: str
    status: str  # PASS / FAIL / ERROR
    message: str = ""
    seconds: float = 0.0


@dataclass
class Ctx:
    """Dữ liệu dùng chung giữa các ca test."""
    base_url: str
    owner: Api
    staff: Api
    cashier: Api
    owner_b: Api | None
    branch_id: str = ""
    warehouse_id: str = ""
    sku: dict = field(default_factory=dict)
    unit_price: Decimal = Decimal(0)
    order_id: str = ""
    order_item_id: str = ""
    sold_qty: Decimal = Decimal(3)
    draft: dict = field(default_factory=dict)       # phiếu Draft đầu tiên (sẽ được xác nhận)
    draft_key: str = ""
    draft_payload: dict = field(default_factory=dict)
    on_hand_before_confirm: Decimal = Decimal(0)


def new_key() -> str:
    return str(uuid.uuid4())


def return_payload(ctx: Ctx, quantity, refund=None, reason="Lỗi sản phẩm", note=None, order_item_id=None, order_id=None):
    item = {"orderItemId": order_item_id or ctx.order_item_id, "quantity": quantity}
    if refund is not None:
        item["refundAmount"] = refund
    payload = {
        "orderId": order_id or ctx.order_id,
        "warehouseId": ctx.warehouse_id,
        "reason": reason,
        "items": [item],
    }
    if note is not None:
        payload["note"] = note
    return payload


def create_return(api: Api, payload: dict, key: str | None = None):
    headers = {"Idempotency-Key": key} if key else None
    return api.post("/api/sales-returns", json=payload, headers=headers)


def branch_on_hand(ctx: Ctx) -> Decimal:
    resp = ctx.owner.get(f"/api/inventory/balances/sku/{ctx.sku['variantId']}/branch/{ctx.branch_id}")
    if resp.status_code == 404:
        return Decimal(0)
    expect_status(resp, 200)
    return dec(resp.json()["onHand"])


def returnable(ctx: Ctx) -> dict:
    resp = ctx.owner.get(f"/api/orders/{ctx.order_id}/returnable-items")
    expect_status(resp, 200)
    items = {i["orderItemId"]: i for i in resp.json()}
    check(ctx.order_item_id in items, "không thấy dòng đơn hàng trong returnable-items")
    return items[ctx.order_item_id]


# ---------------------------------------------------------------------------
# Chuẩn bị dữ liệu: bán một đơn POS mới
# ---------------------------------------------------------------------------

def setup(ctx: Ctx, sku_query: str):
    resp = ctx.owner.get("/api/warehouses")
    expect_status(resp, 200)
    warehouses = [w for w in resp.json() if w.get("isActive", True)]
    check(warehouses, "tenant không có kho nào đang hoạt động")
    ctx.warehouse_id = warehouses[0]["id"]
    ctx.branch_id = warehouses[0]["branchId"]

    resp = ctx.owner.get("/api/pos/search", params={"query": sku_query, "branchId": ctx.branch_id})
    expect_status(resp, 200)
    candidates = []
    for s in resp.json():
        base_unit = next((u for u in s.get("units", []) if u.get("isBase")), None)
        available = s.get("available")
        if base_unit and base_unit.get("salePrice") and (available is None or dec(available) >= ctx.sold_qty):
            candidates.append((s, base_unit))
    check(candidates, f"không tìm thấy SKU '{sku_query}' có giá bán và đủ tồn để bán {ctx.sold_qty}")
    ctx.sku, unit = candidates[0]
    ctx.unit_price = dec(unit["salePrice"])

    total = ctx.unit_price * ctx.sold_qty
    key = new_key()
    checkout = {
        "branchId": ctx.branch_id,
        "channel": "POS",
        "paymentMethod": "Cash",
        "amountGiven": float(total),
        "changeDue": 0,
        "requestKey": key,
        "totalAmount": float(total),
        "items": [{
            "skuId": ctx.sku["variantId"],
            "skuCode": ctx.sku["sku"],
            "productName": ctx.sku["productName"],
            "variantName": ctx.sku.get("variantName") or "",
            "unitId": unit["unitId"],
            "quantity": float(ctx.sold_qty),
            "unitPrice": float(ctx.unit_price),
        }],
    }
    resp = ctx.owner.post("/api/pos/checkout", json=checkout, headers={"Idempotency-Key": key})
    check(resp.status_code in (200, 201), f"POS checkout thất bại: HTTP {resp.status_code} {resp.text[:300]}")
    body = resp.json()
    ctx.order_id = body.get("orderId") or body.get("id") or (body.get("order") or {}).get("id")
    check(ctx.order_id, f"không đọc được orderId từ phản hồi checkout: {str(body)[:300]}")

    resp = ctx.owner.get(f"/api/orders/{ctx.order_id}")
    expect_status(resp, 200)
    order = resp.json()
    check(order["status"] in ("Confirmed", "Completed"), f"đơn POS ở trạng thái {order['status']}")
    ctx.order_item_id = order["items"][0]["id"]
    print(f"  Đơn POS {order.get('number')} | SKU {ctx.sku['sku']} x{ctx.sold_qty} | đơn giá {ctx.unit_price}")


# ---------------------------------------------------------------------------
# Các ca kiểm thử
# ---------------------------------------------------------------------------

def tc01_returnable_items(ctx: Ctx):
    item = returnable(ctx)
    check(dec(item["soldQuantity"]) == ctx.sold_qty, f"soldQuantity = {item['soldQuantity']}")
    check(dec(item["returnedQuantity"]) == 0, f"returnedQuantity = {item['returnedQuantity']}")
    check(dec(item["returnableQuantity"]) == ctx.sold_qty, f"returnableQuantity = {item['returnableQuantity']}")
    check(dec(item["unitPrice"]) == ctx.unit_price, f"unitPrice = {item['unitPrice']}")


def tc02_missing_idempotency_key(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1))
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc03_invalid_idempotency_key(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1), key="khong-phai-guid")
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc04_empty_reason(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, reason="   "), key=new_key())
    expect_status(resp, 400)


def tc05_empty_items(ctx: Ctx):
    payload = return_payload(ctx, 1)
    payload["items"] = []
    resp = create_return(ctx.owner, payload, key=new_key())
    expect_status(resp, 400)


def tc06_zero_quantity(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 0), key=new_key())
    expect_status(resp, 400)


def tc07_quantity_exceeded(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, float(ctx.sold_qty + 1)), key=new_key())
    expect_status(resp, 422, "RETURN_QUANTITY_EXCEEDED")


def tc08_refund_exceeded_line(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, refund=float(ctx.unit_price + 1)), key=new_key())
    expect_status(resp, 422, "REFUND_AMOUNT_EXCEEDED")


def tc09_negative_refund(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, refund=-1000), key=new_key())
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc10_refund_too_many_decimals(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, refund=1000.123), key=new_key())
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc11_duplicate_lines(ctx: Ctx):
    payload = return_payload(ctx, 1)
    payload["items"].append({"orderItemId": ctx.order_item_id, "quantity": 1})
    resp = create_return(ctx.owner, payload, key=new_key())
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc12_order_item_not_in_order(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, order_item_id=new_key()), key=new_key())
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc13_order_not_found(ctx: Ctx):
    resp = create_return(ctx.owner, return_payload(ctx, 1, order_id=new_key()), key=new_key())
    expect_status(resp, 404, "ORDER_NOT_FOUND")


def tc14_warehouse_not_found(ctx: Ctx):
    payload = return_payload(ctx, 1)
    payload["warehouseId"] = new_key()
    resp = create_return(ctx.owner, payload, key=new_key())
    expect_status(resp, 422, "RETURN_WAREHOUSE_MISMATCH")


def tc15_cashier_forbidden(ctx: Ctx):
    resp = create_return(ctx.cashier, return_payload(ctx, 1), key=new_key())
    expect_status(resp, 403)
    resp = ctx.cashier.get(f"/api/orders/{ctx.order_id}/returnable-items")
    expect_status(resp, 403)


def tc16_unauthenticated(ctx: Ctx):
    anonymous = Api(ctx.base_url)
    resp = create_return(anonymous, return_payload(ctx, 1), key=new_key())
    expect_status(resp, 401)


def tc17_create_draft(ctx: Ctx):
    ctx.on_hand_before_confirm = branch_on_hand(ctx)
    ctx.draft_key = new_key()
    ctx.draft_payload = return_payload(ctx, 1, note="Script Python CS-01")
    resp = create_return(ctx.owner, ctx.draft_payload, key=ctx.draft_key)
    expect_status(resp, 201)
    ctx.draft = resp.json()
    d = ctx.draft
    check(d["status"] == "Draft", f"status = {d['status']}")
    check(re.fullmatch(r"RT-\d{8}-\d{4}", d["number"]) is not None, f"số phiếu sai định dạng: {d['number']}")
    check(d["orderId"] == ctx.order_id, "orderId không khớp")
    check(d["requestKey"] == ctx.draft_key, "requestKey không khớp Idempotency-Key")
    # Không nhập refundAmount -> mặc định = SL x đơn giá
    check(dec(d["totalRefundAmount"]) == ctx.unit_price, f"totalRefundAmount = {d['totalRefundAmount']}")
    check(len(d["items"]) == 1 and dec(d["items"][0]["quantity"]) == 1, "dòng phiếu trả sai")


def tc18_draft_does_not_touch_stock(ctx: Ctx):
    check(branch_on_hand(ctx) == ctx.on_hand_before_confirm, "phiếu Draft đã làm thay đổi tồn kho")
    item = returnable(ctx)
    check(dec(item["returnableQuantity"]) == ctx.sold_qty, "phiếu Draft đã bị tính vào số lượng đã trả")


def tc19_idempotent_replay(ctx: Ctx):
    resp = create_return(ctx.owner, ctx.draft_payload, key=ctx.draft_key)
    expect_status(resp, 200)
    check(resp.json()["id"] == ctx.draft["id"], "replay tạo ra phiếu mới thay vì trả phiếu cũ")
    check(resp.json()["number"] == ctx.draft["number"], "replay trả số phiếu khác")


def tc20_idempotency_payload_mismatch(ctx: Ctx):
    payload = dict(ctx.draft_payload, reason="Khách đổi ý")
    resp = create_return(ctx.owner, payload, key=ctx.draft_key)
    expect_status(resp, 409, "IDEMPOTENCY_PAYLOAD_MISMATCH")


def tc21_get_by_id_and_list(ctx: Ctx):
    resp = ctx.owner.get(f"/api/sales-returns/{ctx.draft['id']}")
    expect_status(resp, 200)
    check(resp.json()["number"] == ctx.draft["number"], "GET chi tiết trả về phiếu khác")
    resp = ctx.owner.get("/api/sales-returns", params={"orderId": ctx.order_id})
    expect_status(resp, 200)
    ids = [r["id"] for r in resp.json()["items"]]
    check(ctx.draft["id"] in ids, "danh sách lọc theo orderId không chứa phiếu vừa tạo")
    resp = ctx.owner.get(f"/api/sales-returns/{new_key()}")
    expect_status(resp, 404, "SALES_RETURN_NOT_FOUND")


def tc22_tenant_isolation(ctx: Ctx):
    if ctx.owner_b is None:
        raise CheckFailed("không đăng nhập được owner@shopb.local để kiểm tra")
    resp = ctx.owner_b.get(f"/api/sales-returns/{ctx.draft['id']}")
    expect_status(resp, 404)
    resp = ctx.owner_b.get(f"/api/orders/{ctx.order_id}/returnable-items")
    expect_status(resp, 404)
    resp = ctx.owner_b.post(f"/api/sales-returns/{ctx.draft['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 404)


def tc23_confirm_requires_refund_method(ctx: Ctx):
    resp = ctx.owner.post(f"/api/sales-returns/{ctx.draft['id']}/confirm", json={})
    expect_status(resp, 400, "VALIDATION_ERROR")
    resp = ctx.owner.post(f"/api/sales-returns/{ctx.draft['id']}/confirm", json={"refundMethod": "Bitcoin"})
    expect_status(resp, 400, "VALIDATION_ERROR")


def tc24_confirm_success(ctx: Ctx):
    resp = ctx.owner.post(f"/api/sales-returns/{ctx.draft['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 200)
    d = resp.json()
    check(d["status"] == "Confirmed", f"status = {d['status']}")
    check(d["refundMethod"] == "Cash", f"refundMethod = {d['refundMethod']}")
    check(d.get("confirmedAt"), "thiếu confirmedAt")
    after = branch_on_hand(ctx)
    check(after == ctx.on_hand_before_confirm + 1, f"tồn chi nhánh {ctx.on_hand_before_confirm} -> {after}, mong đợi +1")


def tc25_ledger_in_entry(ctx: Ctx):
    item_ids = {i["id"] for i in ctx.draft["items"]}
    resp = ctx.owner.get("/api/inventory/ledger", params={
        "skuId": ctx.sku["variantId"], "branchId": ctx.branch_id, "type": "IN", "pageSize": 50,
    })
    expect_status(resp, 200)
    rows = [r for r in resp.json()["items"] if r.get("referenceId") in item_ids]
    check(len(rows) == 1, f"mong đợi đúng 1 dòng sổ IN cho phiếu trả, tìm thấy {len(rows)}")
    row = rows[0]
    check(row["documentType"] == "SalesReturn", f"documentType = {row['documentType']}")
    check(dec(row["quantity"]) == 1, f"quantity sổ = {row['quantity']}")
    check(row["warehouseId"] == ctx.warehouse_id, "dòng sổ không ghi đúng kho nhận")
    check(dec(row["unitCost"]) == dec(ctx.draft["items"][0]["originalUnitCost"]),
          "giá vốn dòng sổ khác giá vốn gốc của đơn")


def tc26_confirm_twice_is_idempotent(ctx: Ctx):
    before = branch_on_hand(ctx)
    resp = ctx.owner.post(f"/api/sales-returns/{ctx.draft['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 200)
    check(resp.json()["status"] == "Confirmed", "trạng thái thay đổi sau lần xác nhận thứ hai")
    check(branch_on_hand(ctx) == before, "xác nhận lần hai làm tăng tồn thêm lần nữa")


def tc27_cannot_cancel_confirmed(ctx: Ctx):
    resp = ctx.owner.post(f"/api/sales-returns/{ctx.draft['id']}/cancel")
    expect_status(resp, 409, "INVALID_STATE_TRANSITION")


def tc28_returnable_decreased(ctx: Ctx):
    item = returnable(ctx)
    check(dec(item["returnedQuantity"]) == 1, f"returnedQuantity = {item['returnedQuantity']}")
    check(dec(item["returnableQuantity"]) == ctx.sold_qty - 1, f"returnableQuantity = {item['returnableQuantity']}")


def tc29_staff_partial_refund_then_cancel(ctx: Ctx):
    partial = float(ctx.unit_price / 2)
    resp = create_return(ctx.staff, return_payload(ctx, 1, refund=partial, reason="Khách đổi ý"), key=new_key())
    expect_status(resp, 201)
    d = resp.json()
    check(dec(d["totalRefundAmount"]) == dec(partial), f"totalRefundAmount = {d['totalRefundAmount']}")
    before = branch_on_hand(ctx)
    resp = ctx.staff.post(f"/api/sales-returns/{d['id']}/cancel")
    expect_status(resp, 200)
    check(resp.json()["status"] == "Cancelled", f"status = {resp.json()['status']}")
    check(branch_on_hand(ctx) == before, "hủy phiếu Draft làm thay đổi tồn")
    resp = ctx.staff.post(f"/api/sales-returns/{d['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 409, "INVALID_STATE_TRANSITION")
    resp = ctx.staff.post(f"/api/sales-returns/{d['id']}/cancel")
    expect_status(resp, 409, "INVALID_STATE_TRANSITION")


def tc30_return_rest_then_block(ctx: Ctx):
    rest = ctx.sold_qty - 1
    resp = create_return(ctx.owner, return_payload(ctx, float(rest), reason="Sai hàng"), key=new_key())
    expect_status(resp, 201)
    rid = resp.json()["id"]
    resp = ctx.owner.post(f"/api/sales-returns/{rid}/confirm", json={"refundMethod": "BankTransfer"})
    expect_status(resp, 200)
    check(resp.json()["refundMethod"] == "BankTransfer", "refundMethod không lưu BankTransfer")
    item = returnable(ctx)
    check(dec(item["returnableQuantity"]) == 0, f"returnableQuantity = {item['returnableQuantity']}")
    resp = create_return(ctx.owner, return_payload(ctx, 1), key=new_key())
    expect_status(resp, 422, "RETURN_QUANTITY_EXCEEDED")


def tc31_concurrent_drafts_cannot_over_return(ctx: Ctx):
    """Hai phiếu Draft cùng trả 1 dòng: khi đã trả hết thì phiếu Draft còn lại không được xác nhận."""
    # Bán đơn mới 1 sản phẩm để kiểm tra độc lập
    total = ctx.unit_price
    key = new_key()
    checkout = {
        "branchId": ctx.branch_id, "channel": "POS", "paymentMethod": "Cash",
        "amountGiven": float(total), "changeDue": 0, "requestKey": key, "totalAmount": float(total),
        "items": [{
            "skuId": ctx.sku["variantId"], "skuCode": ctx.sku["sku"], "productName": ctx.sku["productName"],
            "variantName": ctx.sku.get("variantName") or "", "quantity": 1, "unitPrice": float(ctx.unit_price),
        }],
    }
    resp = ctx.owner.post("/api/pos/checkout", json=checkout, headers={"Idempotency-Key": key})
    check(resp.status_code in (200, 201), f"checkout phụ thất bại: {resp.status_code}")
    body = resp.json()
    order_id = body.get("orderId") or body.get("id") or (body.get("order") or {}).get("id")
    order = ctx.owner.get(f"/api/orders/{order_id}").json()
    oi = order["items"][0]["id"]

    r1 = create_return(ctx.owner, return_payload(ctx, 1, order_id=order_id, order_item_id=oi), key=new_key())
    r2 = create_return(ctx.owner, return_payload(ctx, 1, order_id=order_id, order_item_id=oi), key=new_key())
    expect_status(r1, 201)
    expect_status(r2, 201)
    resp = ctx.owner.post(f"/api/sales-returns/{r1.json()['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 200)
    resp = ctx.owner.post(f"/api/sales-returns/{r2.json()['id']}/confirm", json={"refundMethod": "Cash"})
    expect_status(resp, 422, "RETURN_QUANTITY_EXCEEDED")


TEST_CASES = [
    ("TC01", "Xem dòng hàng còn được trả của đơn vừa bán", tc01_returnable_items),
    ("TC02", "Thiếu Idempotency-Key -> 400", tc02_missing_idempotency_key),
    ("TC03", "Idempotency-Key không phải GUID -> 400", tc03_invalid_idempotency_key),
    ("TC04", "Lý do trả hàng rỗng -> 400", tc04_empty_reason),
    ("TC05", "Không có dòng mặt hàng -> 400", tc05_empty_items),
    ("TC06", "Số lượng trả = 0 -> 400", tc06_zero_quantity),
    ("TC07", "Trả vượt số lượng đã bán -> 422", tc07_quantity_exceeded),
    ("TC08", "Tiền hoàn vượt SL x đơn giá -> 422", tc08_refund_exceeded_line),
    ("TC09", "Tiền hoàn âm -> 400", tc09_negative_refund),
    ("TC10", "Tiền hoàn quá 2 chữ số thập phân -> 400", tc10_refund_too_many_decimals),
    ("TC11", "Trùng dòng đơn hàng trong một phiếu -> 400", tc11_duplicate_lines),
    ("TC12", "Dòng hàng không thuộc đơn gốc -> 400", tc12_order_item_not_in_order),
    ("TC13", "Đơn hàng không tồn tại -> 404", tc13_order_not_found),
    ("TC14", "Kho nhận không thuộc chi nhánh của đơn -> 422", tc14_warehouse_not_found),
    ("TC15", "Cashier không được lập phiếu trả -> 403", tc15_cashier_forbidden),
    ("TC16", "Không có token -> 401", tc16_unauthenticated),
    ("TC17", "Owner lập phiếu Draft hợp lệ -> 201", tc17_create_draft),
    ("TC18", "Phiếu Draft chưa làm thay đổi tồn kho", tc18_draft_does_not_touch_stock),
    ("TC19", "Gửi lại cùng key, cùng payload -> 200, cùng phiếu", tc19_idempotent_replay),
    ("TC20", "Cùng key, khác payload -> 409", tc20_idempotency_payload_mismatch),
    ("TC21", "Xem chi tiết, danh sách, phiếu không tồn tại", tc21_get_by_id_and_list),
    ("TC22", "Tenant khác không thấy và không xác nhận được phiếu", tc22_tenant_isolation),
    ("TC23", "Xác nhận thiếu / sai phương thức hoàn tiền -> 400", tc23_confirm_requires_refund_method),
    ("TC24", "Xác nhận phiếu: Confirmed, tồn +1", tc24_confirm_success),
    ("TC25", "Sổ kho có đúng 1 dòng IN theo giá vốn gốc", tc25_ledger_in_entry),
    ("TC26", "Xác nhận lần hai không cộng tồn lần nữa", tc26_confirm_twice_is_idempotent),
    ("TC27", "Không hủy được phiếu đã xác nhận -> 409", tc27_cannot_cancel_confirmed),
    ("TC28", "Số lượng còn được trả giảm sau xác nhận", tc28_returnable_decreased),
    ("TC29", "Staff lập phiếu hoàn một phần rồi hủy", tc29_staff_partial_refund_then_cancel),
    ("TC30", "Trả hết phần còn lại, sau đó bị chặn", tc30_return_rest_then_block),
    ("TC31", "Hai phiếu Draft cùng dòng: phiếu thứ hai bị chặn khi xác nhận", tc31_concurrent_drafts_cannot_over_return),
]

# Các ca phụ thuộc dữ liệu từ ca trước; nếu ca gốc lỗi thì đánh dấu FAIL theo
DEPENDS_ON_DRAFT = {"TC18", "TC19", "TC20", "TC21", "TC22", "TC23", "TC24", "TC25", "TC26", "TC27", "TC28"}


# ---------------------------------------------------------------------------
# Chạy
# ---------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(description="Kiểm thử API trả hàng (CS-01) OISMS")
    parser.add_argument("--base-url", default=os.environ.get("OISMS_BASE_URL", "http://localhost:5000"))
    parser.add_argument("--password", default=os.environ.get("OISMS_PASSWORD"))
    parser.add_argument("--sku-query", default="POLO")
    parser.add_argument("-k", "--only", help="Chỉ chạy các ca có mã chứa chuỗi này, ví dụ TC2")
    parser.add_argument("-v", "--verbose", action="store_true", help="In từng request")
    args = parser.parse_args()

    if not args.password:
        print("Chưa có mật khẩu tài khoản demo. Đặt biến OISMS_PASSWORD hoặc truyền --password.")
        return 2

    print(f"== Kiểm thử Trả hàng (CS-01) tại {args.base_url} ==")
    try:
        health = requests.get(args.base_url.rstrip("/") + "/health/ready", timeout=10)
        print(f"  /health/ready -> HTTP {health.status_code}")
        owner = login(args.base_url, ACCOUNTS["owner"], args.password, args.verbose)
        staff = login(args.base_url, ACCOUNTS["staff"], args.password, args.verbose)
        cashier = login(args.base_url, ACCOUNTS["cashier"], args.password, args.verbose)
        try:
            owner_b = login(args.base_url, ACCOUNTS["owner_b"], args.password, args.verbose)
        except RuntimeError:
            owner_b = None
        ctx = Ctx(args.base_url, owner, staff, cashier, owner_b)
        setup(ctx, args.sku_query)
    except Exception as exc:  # noqa: BLE001
        print(f"Không chuẩn bị được dữ liệu test: {exc}")
        return 2

    results: list[Result] = []
    draft_ok = True
    for tc_id, title, fn in TEST_CASES:
        if args.only and args.only not in tc_id:
            continue
        start = time.perf_counter()
        if tc_id in DEPENDS_ON_DRAFT and not draft_ok:
            results.append(Result(tc_id, title, "FAIL", "bỏ qua vì TC17 (tạo phiếu Draft) không thành công"))
            print(f"  [FAIL ] {tc_id} {title} — bỏ qua vì TC17 lỗi")
            continue
        try:
            fn(ctx)
            res = Result(tc_id, title, "PASS")
        except CheckFailed as exc:
            res = Result(tc_id, title, "FAIL", str(exc))
        except Exception as exc:  # noqa: BLE001
            res = Result(tc_id, title, "ERROR", f"{type(exc).__name__}: {exc}")
            if args.verbose:
                traceback.print_exc()
        res.seconds = time.perf_counter() - start
        if tc_id == "TC17" and res.status != "PASS":
            draft_ok = False
        results.append(res)
        line = f"  [{res.status:<5}] {tc_id} {title} ({res.seconds:.2f}s)"
        if res.message:
            line += f"\n          -> {res.message}"
        print(line)

    passed = sum(r.status == "PASS" for r in results)
    failed = len(results) - passed
    print(f"\nKết quả: PASS {passed}/{len(results)}, FAIL/ERROR {failed}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
