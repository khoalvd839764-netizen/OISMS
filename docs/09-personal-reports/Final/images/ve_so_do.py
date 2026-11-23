"""Vẽ các sơ đồ nghiệp vụ cho báo cáo Final (mục 7.x / 8.x).

Chạy lại sau khi sửa nội dung:
    python3 docs/09-personal-reports/Final/images/ve_so_do.py
Ảnh PNG được ghi cạnh file này.
"""

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyArrowPatch, FancyBboxPatch, Polygon

OUT = Path(__file__).resolve().parent
plt.rcParams["font.family"] = ["Noto Sans", "DejaVu Sans"]

# Bảng màu theo loại đối tượng: (nền, viền)
C = {
    "doc": ("#DCEBFA", "#2F6DB5"),     # chứng từ / bước xử lý
    "stock": ("#DFF3E3", "#2E7D32"),   # tồn kho
    "ledger": ("#FFF1D6", "#C77700"),  # sổ cái
    "report": ("#EEE6F7", "#6A4C9C"),  # báo cáo / thông báo
    "bad": ("#FBE3E3", "#C62828"),     # hủy / từ chối
    "ext": ("#EFEFEF", "#616161"),     # bên ngoài
    "tx": ("#F7FAFF", "#90A4C0"),      # khung transaction
}
EDGE = "#37474F"


def canvas(w, h):
    fig, ax = plt.subplots(figsize=(w / 10, h / 10))
    ax.set_xlim(0, w)
    ax.set_ylim(0, h)
    ax.set_aspect("equal")
    ax.axis("off")
    return fig, ax


def box(ax, cx, cy, w, h, text, kind="doc", size=10, bold_first=True, dashed=False):
    face, edge = C[kind]
    ax.add_patch(
        FancyBboxPatch(
            (cx - w / 2, cy - h / 2), w, h,
            boxstyle="round,pad=0.4,rounding_size=1.6",
            fc=face, ec=edge, lw=1.6, ls="--" if dashed else "-", zorder=2,
        )
    )
    lines = text.split("\n")
    if bold_first and len(lines) > 1:
        title_top = cy + h / 2 - 1.3
        ax.text(cx, title_top, lines[0], ha="center", va="top",
                fontsize=size, fontweight="bold", color=edge, zorder=3)
        body_center = ((title_top - 2.4) + (cy - h / 2 + 0.8)) / 2
        ax.text(cx, body_center, "\n".join(lines[1:]), ha="center", va="center",
                fontsize=size - 1, color="#212121", zorder=3, linespacing=1.35)
    else:
        ax.text(cx, cy, text, ha="center", va="center", fontsize=size,
                fontweight="bold" if bold_first else "normal", color=edge, zorder=3,
                linespacing=1.35)
    return (cx, cy, w, h)


def diamond(ax, cx, cy, w, h, text, size=10):
    pts = [(cx, cy + h / 2), (cx + w / 2, cy), (cx, cy - h / 2), (cx - w / 2, cy)]
    ax.add_patch(Polygon(pts, closed=True, fc="#FFFBE6", ec="#B08900", lw=1.6, zorder=2))
    ax.text(cx, cy, text, ha="center", va="center", fontsize=size, zorder=3, linespacing=1.3)


def arrow(ax, p1, p2, label=None, color=EDGE, style="-|>", ls="-", rad=0.0,
          lpos=0.5, loff=(0, 0), size=9, lcolor=None, ha="center"):
    ax.add_patch(
        FancyArrowPatch(p1, p2, arrowstyle=style, mutation_scale=16, lw=1.6,
                        color=color, ls=ls, connectionstyle=f"arc3,rad={rad}", zorder=1,
                        shrinkA=2, shrinkB=2)
    )
    if label:
        x = p1[0] + (p2[0] - p1[0]) * lpos + loff[0]
        y = p1[1] + (p2[1] - p1[1]) * lpos + loff[1]
        ax.text(x, y, label, ha=ha, va="center", fontsize=size, color=lcolor or color,
                zorder=4, linespacing=1.3,
                bbox=dict(boxstyle="round,pad=0.25", fc="white", ec="none", alpha=0.9))


def top(b):
    return (b[0], b[1] + b[3] / 2)


def bottom(b):
    return (b[0], b[1] - b[3] / 2)


def left(b):
    return (b[0] - b[2] / 2, b[1])


def right(b):
    return (b[0] + b[2] / 2, b[1])


def legend(ax, x, y, items, size=9):
    for i, (kind, label) in enumerate(items):
        face, edge = C[kind]
        ax.add_patch(FancyBboxPatch((x + i * 22, y - 1.5), 3.5, 3,
                                    boxstyle="round,pad=0.2", fc=face, ec=edge, lw=1.2))
        ax.text(x + i * 22 + 5, y, label, va="center", fontsize=size, color="#424242")


def save(fig, name):
    fig.savefig(OUT / name, dpi=200, bbox_inches="tight", pad_inches=0.15, facecolor="white")
    plt.close(fig)
    print("Đã ghi", name)


# ---------------------------------------------------------------- 7.1
def tong_the():
    fig, ax = canvas(130, 98)
    ncc = box(ax, 18, 90, 24, 7, "Nhà cung cấp", "ext", bold_first=True)
    po = box(ax, 18, 75, 30, 11, "Đơn mua\nStaff đề nghị (không thấy giá)\nOwner duyệt giá, phí, VAT")
    gr = box(ax, 18, 57, 30, 9, "Phiếu nhập kho\nTính lại giá vốn WAC")
    tf = box(ax, 18, 36, 30, 11, "Chuyển kho 2 bước\nKho A → Đang chuyển → Kho B\nghi OUT ở A, IN ở B")

    kho = box(ax, 65, 52, 26, 22, "TỒN KHO\non_hand (tồn thực)\nreserved (đã giữ chỗ)\n\navailable =\non_hand − reserved",
              "stock", size=11)

    pos = box(ax, 112, 78, 30, 9, "Bán tại quầy POS\nCashier, 1 transaction")
    onl = box(ax, 112, 64, 30, 9, "Đơn online đã duyệt xuất\nStaff / Owner")
    ret = box(ax, 112, 47, 30, 9, "Phiếu trả hàng\nnhập lại theo giá vốn lúc bán")
    cnt = box(ax, 112, 33, 30, 9, "Kiểm kê\nOwner duyệt chênh lệch")
    kh = box(ax, 112, 91, 22, 6, "Khách hàng", "ext")

    ledger = box(ax, 65, 20, 56, 10,
                 "Sổ cái inventory_transactions\nchỉ INSERT · BalanceAfter · ReferenceId · SequenceNo",
                 "ledger")
    rep = box(ax, 65, 5, 92, 7,
              "Báo cáo: Doanh thu thuần · COGS · Lãi gộp · Giá trị tồn · Thẻ kho S12/S10 · Gợi ý nhập hàng",
              "report", bold_first=False, size=10)

    arrow(ax, bottom(ncc), top(po))
    arrow(ax, bottom(po), top(gr), "tạo phiếu nhập", loff=(2, 0), ha="left")
    arrow(ax, right(gr), (kho[0] - 13, 57), "IN", color="#2E7D32")
    arrow(ax, right(tf), (kho[0] - 13, 46), "OUT / IN", color="#2E7D32", rad=0.0)

    arrow(ax, (kho[0] + 13, 60), left(pos), "OUT", color="#C62828", lpos=0.45)
    arrow(ax, (kho[0] + 13, 56), left(onl), "OUT", color="#C62828", lpos=0.45)
    arrow(ax, left(ret), (kho[0] + 13, 49), "IN", color="#2E7D32", lpos=0.5)
    arrow(ax, left(cnt), (kho[0] + 13, 45), "IN / OUT", color="#2E7D32", lpos=0.5, style="<|-|>")
    arrow(ax, top(pos), bottom(kh))

    arrow(ax, bottom(kho), top(ledger), "mọi biến động tồn đều ghi sổ", loff=(0, 0))
    arrow(ax, bottom(ledger), top(rep))

    legend(ax, 40, 95, [("doc", "Chứng từ"), ("stock", "Tồn kho"), ("ledger", "Sổ cái")])
    save(fig, "01-tong-the-hang-hoa-chung-tu.png")


# ---------------------------------------------------------------- 7.2
def vong_doi_don():
    fig, ax = canvas(130, 62)
    y = 46
    d = box(ax, 11, y, 18, 8, "Draft", bold_first=False, size=12)
    r = box(ax, 42, y, 20, 8, "Reserved", bold_first=False, size=12)
    cf = box(ax, 86, y, 20, 8, "Confirmed", bold_first=False, size=12)
    cp = box(ax, 118, y, 20, 8, "Completed", "stock", bold_first=False, size=12)
    cx = box(ax, 29, 12, 22, 8, "Cancelled", "bad", bold_first=False, size=12)
    rt = box(ax, 108, 12, 36, 12, "Phiếu trả hàng\nghi IN theo giá vốn gốc,\ntính lại WAC", "doc", dashed=True)

    arrow(ax, right(d), left(r), "giữ chỗ\nreserved + SL", lpos=0.5, loff=(0, 7))
    arrow(ax, right(r), left(cf), "duyệt xuất\non_hand − SL, reserved − SL\nghi OUT, chụp CostPrice",
          lpos=0.5, loff=(0, 9))
    arrow(ax, right(cf), left(cp), "giao xong", loff=(0, 5))
    arrow(ax, bottom(d), (cx[0] - 6, 16), "hủy", color="#C62828", loff=(-4, 0))
    arrow(ax, bottom(r), (cx[0] + 6, 16), "hủy hoặc\nhết hạn 15 phút\n(Hangfire)", color="#C62828",
          loff=(9, 2), ha="left")
    arrow(ax, bottom(cf), (rt[0] - 10, 17), color="#6A4C9C", ls="--")
    arrow(ax, bottom(cp), (rt[0] + 8, 17), color="#6A4C9C", ls="--")
    ax.text(cx[0], 3.5, "chỉ giảm reserved, không ghi sổ", ha="center", fontsize=9, color="#C62828")
    ax.text(rt[0], 3.5, "đã xuất kho: không hủy được, chỉ lập phiếu trả", ha="center",
            fontsize=9, color="#6A4C9C")
    save(fig, "02-vong-doi-don-hang.png")


# ---------------------------------------------------------------- 7.3
def chong_ban_vuot():
    fig, ax = canvas(130, 92)
    srcs = ["POS quầy 1", "POS quầy 2", "Webhook sàn", "Admin"]
    api = box(ax, 36, 60, 12, 8, "API", "doc", bold_first=False, size=11)
    for i, s in enumerate(srcs):
        b = box(ax, 10, 81 - i * 14, 16, 7, s, "ext", bold_first=False, size=10)
        arrow(ax, right(b), left(api))

    # Khung transaction
    ax.add_patch(FancyBboxPatch((50, 6), 78, 84, boxstyle="round,pad=0.4,rounding_size=2",
                                fc=C["tx"][0], ec=C["tx"][1], lw=1.4, ls="--", zorder=0))
    ax.text(52, 87.5, "Một transaction PostgreSQL", fontsize=10, color="#5C6F8A", fontweight="bold")

    b1 = box(ax, 89, 80, 64, 6, "BEGIN TRANSACTION", bold_first=False, size=10)
    b2 = box(ax, 89, 69, 64, 9,
             "Khóa theo (TenantId, BranchId, SkuId)\npg_advisory_xact_lock → các request xếp hàng")
    b3 = box(ax, 89, 57, 64, 7, "SELECT … FOR UPDATE trên inventory_balances", bold_first=False, size=10)
    diamond(ax, 89, 43, 46, 13, "available = on_hand − reserved\n≥ số lượng mua ?")
    ok = box(ax, 70, 19, 34, 14, "Đủ hàng\nCập nhật tồn + ghi sổ\nCOMMIT\nPhát SignalR sau commit", "stock")
    no = box(ax, 110, 19, 30, 14, "Thiếu hàng\nROLLBACK\n409 INSUFFICIENT_STOCK", "bad")

    arrow(ax, right(api), left(b1))
    arrow(ax, bottom(b1), top(b2))
    arrow(ax, bottom(b2), top(b3))
    arrow(ax, bottom(b3), (89, 49.5))
    arrow(ax, (75, 40), top(ok), "Có", color="#2E7D32", lpos=0.4, loff=(-3, 0))
    arrow(ax, (103, 40), top(no), "Không", color="#C62828", lpos=0.4, loff=(4, 0))

    ax.text(2, 18, "• Đơn nhiều SKU: khóa theo thứ tự\n  SkuId tăng dần → không deadlock.\n\n"
                   "• Gửi lại cùng RequestKey và cùng\n  payload → trả kết quả cũ;\n  khác payload → 409.",
            fontsize=9, color="#37474F", va="center", linespacing=1.4)
    save(fig, "03-chong-ban-vuot.png")


# ---------------------------------------------------------------- 7.4
def mua_hang_wac():
    fig, ax = canvas(100, 118)
    x, w = 50, 78
    steps = [
        ("Staff · Đề nghị mua (PendingApproval)", "Chỉ chọn hàng và số lượng, không thấy giá", "doc", 9),
        ("Owner · Duyệt đơn mua (Approved)", "Chọn NCC, giá, phí vận chuyển, chiết khấu, VAT\nGửi kèm version để chống ghi đè", "doc", 11),
        ("Phiếu nhập kho từ đơn mua → Xác nhận", "Đơn mua: Approved → PartiallyReceived → Received → Closed", "doc", 9),
        ("Phân bổ chi phí theo VAS 02", "Phí (+) và chiết khấu (−) chia theo tỷ lệ giá trị từng dòng\nVAT để ngoài giá vốn", "ledger", 11),
        ("Quy đổi về đơn vị gốc", "BaseQty = SL × hệ số\nGiá vốn 1 đơn vị gốc = đơn giá sau phân bổ ÷ hệ số", "ledger", 11),
        ("Tính lại giá vốn bình quân (WAC)", "WAC mới = (Tồn cũ × WAC cũ + BaseQty × giá nhập) ÷ (Tồn cũ + BaseQty)\nTồn cũ ≤ 0 → lấy luôn giá nhập", "ledger", 11),
        ("Cập nhật tồn và ghi sổ", "on_hand += BaseQty · ghi dòng IN (UnitCost, BalanceAfter)", "stock", 9),
    ]
    y = 110
    prev = None
    for title, body, kind, h in steps:
        b = box(ax, x, y - h / 2, w, h, f"{title}\n{body}", kind, size=10)
        if prev:
            arrow(ax, bottom(prev), top(b))
        prev = b
        y -= h + 6
    save(fig, "04-mua-hang-nhap-kho-wac.png")


# ---------------------------------------------------------------- 7.5a
def chuyen_kho():
    fig, ax = canvas(100, 74)
    x, w = 42, 60
    a = box(ax, x, 66, w, 9, "Draft\nTồn hai kho chưa đổi · còn hủy được")
    b = box(ax, x, 40, w, 15, "InTransit (đang chuyển)\nKho A: on_hand − SL, ghi OUT\nChụp UnitCost = WAC của Kho A\nTừ đây không hủy được", "stock")
    c = box(ax, x, 12, w, 11, "Received\nKho B: on_hand + SL, ghi IN\nTính lại WAC Kho B theo UnitCost", "stock")
    arrow(ax, bottom(a), top(b), "Bước 1: Kho A bấm Xuất kho", loff=(2, 0), ha="left", size=9)
    arrow(ax, bottom(b), top(c), "Bước 2: Kho B bấm Xác nhận nhập", loff=(2, 0), ha="left", size=9)
    ax.text(x + w / 2 + 3, 40, "Kho B chưa\ncộng tồn khi\nhàng còn trên\nđường", fontsize=9,
            color="#616161", va="center", linespacing=1.3)
    save(fig, "05a-chuyen-kho-hai-buoc.png")


# ---------------------------------------------------------------- 7.5b
def kiem_ke():
    fig, ax = canvas(110, 100)
    x, w = 40, 56
    a = box(ax, x, 93, w, 6, "Draft", bold_first=False, size=11)
    b = box(ax, x, 79, w, 9, "Counting\nChụp tồn sổ sách + SequenceNo sổ cái")
    c = box(ax, x, 63, w, 9, "PendingApproval\nStaff nộp số đếm, không được tự duyệt")
    diamond(ax, x, 44, 52, 14, "Owner duyệt: SKU có giao dịch mới\nsau lúc chụp (drift)?")
    ok = box(ax, x, 15, w, 11, "Confirmed\nGhi bút toán điều chỉnh IN / OUT\ntheo chênh lệch", "stock")
    rc = box(ax, 92, 44, 30, 13, "Chặn duyệt\nĐếm lại đúng các SKU\nbị lệch (Recount)", "bad")

    arrow(ax, bottom(a), top(b), "Mở phiên đếm", loff=(2, 0), ha="left")
    arrow(ax, bottom(b), top(c), "Staff nhập số đếm thực tế", loff=(2, 0), ha="left")
    arrow(ax, bottom(c), (x, 51))
    arrow(ax, (x, 37), top(ok), "Không", color="#2E7D32", loff=(6, 0))
    arrow(ax, (x + 26, 44), left(rc), "Có", color="#C62828", loff=(0, 3))
    arrow(ax, top(rc), right(c), color="#C62828", ls="--", rad=0.25)
    ax.text(83, 60, "nộp lại", fontsize=9, color="#C62828")
    save(fig, "05b-kiem-ke-duyet-chenh-lech.png")


# ---------------------------------------------------------------- 7.6
def webhook():
    fig, ax = canvas(118, 112)
    x, w = 44, 62
    s = box(ax, x, 105, 40, 7, "Sàn TMĐT / marketplace-simulator", "ext", bold_first=False, size=10)
    b1 = box(ax, x, 86, w, 9, "(1) Xác định cửa hàng\nSecret → ShopIntegration → TenantId")
    diamond(ax, x, 67, 62, 15, "(2) Đã có đơn trùng?\nChannel + ExternalShopId\n+ ExternalOrderId", size=9.5)
    dup = box(ax, 100, 67, 28, 11, "Trả đơn cũ\nkhông giữ chỗ lần 2", "report")
    b3 = box(ax, x, 45, w, 11, "(3) Tạo đơn trong 1 transaction\nChuẩn hóa đơn → khóa tồn\n→ đơn Reserved → COMMIT", "stock")
    b4 = box(ax, x, 21, w, 15,
             "(4) Thông báo realtime (sau commit)\nOrderCreated → Admin\nBranchOrderCreated → POS đúng chi nhánh\nLowStockWarning → khi chạm ngưỡng tồn",
             "report")

    arrow(ax, bottom(s), top(b1), "POST /api/webhooks/{channel}\nHeader: X-Webhook-Secret",
          loff=(3, 0), ha="left")
    arrow(ax, bottom(b1), (x, 74.5))
    arrow(ax, (x + 31, 67), left(dup), "Có", color="#6A4C9C", loff=(0, 3))
    arrow(ax, (x, 59.5), top(b3), "Chưa", color="#2E7D32", loff=(6, 0))
    arrow(ax, bottom(b3), top(b4))
    ax.text(x, 6, "Không tin TenantId gửi trong body: tenant chỉ lấy từ secret của kết nối sàn.",
            ha="center", fontsize=9, color="#C62828")
    save(fig, "06-don-san-qua-webhook.png")


if __name__ == "__main__":
    tong_the()
    vong_doi_don()
    chong_ban_vuot()
    mua_hang_wac()
    chuyen_kho()
    kiem_ke()
    webhook()
