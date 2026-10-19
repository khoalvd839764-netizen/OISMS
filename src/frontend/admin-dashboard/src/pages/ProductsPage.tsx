import {
  AlertCircle,
  Barcode,
  ChevronLeft,
  ChevronRight,
  Filter,
  FolderTree,
  MoreHorizontal,
  Plus,
  Power,
  RefreshCw,
  Search,
  Tag,
  Trash2,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { Sku } from "../types/domain";

// Hàm sinh mã vạch chuẩn EAN-13 hợp lệ kèm số kiểm tra Checksum (893 = Mã quốc gia Việt Nam)
function generateEan13(): string {
  const prefix = "893";
  const body = prefix + Math.floor(100000000 + Math.random() * 900000000).toString();
  let sum = 0;
  for (let i = 0; i < 12; i++) {
    sum += parseInt(body[i], 10) * (i % 2 === 0 ? 1 : 3);
  }
  const checksum = (10 - (sum % 10)) % 10;
  return body + checksum;
}

export function ProductsPage() {
  const { user } = useAuth();
  const [query, setQuery] = useState("");
  const [categoryFilter, setCategoryFilter] = useState("");
  const [brandFilter, setBrandFilter] = useState("");
  const [activeFilter, setActiveFilter] = useState<"all" | "active" | "inactive">("all");
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [products, setProducts] = useState<Sku[]>([]);

  // [A05] Phân trang (Pagination)
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Modals
  const [productModal, setProductModal] = useState(false);
  const [categoryModal, setCategoryModal] = useState(false);
  const [brandModal, setBrandModal] = useState(false);

  // Danh mục & Thương hiệu
  const [categories, setCategories] = useState<
    Array<{ id: string; name: string; rawName?: string; code?: string; parentId?: string | null; level?: number }>
  >([]);
  const [brands, setBrands] = useState<Array<{ id: string; name: string; code?: string }>>([]);

  // Form tạo sản phẩm mới
  const [prodName, setProdName] = useState("");
  const [prodCategory, setProdCategory] = useState("");
  const [prodBrand, setProdBrand] = useState("");
  const [prodDesc, setProdDesc] = useState("");
  const [variants, setVariants] = useState<
    Array<{
      variantName: string;
      sku: string;
      barcode: string;
      retailPrice: number;
      wholesalePrice: number;
    }>
  >([
    {
      variantName: "Mặc định",
      sku: `SKU-${Date.now().toString().slice(-5)}`,
      barcode: generateEan13(),
      retailPrice: 150000,
      wholesalePrice: 100000,
    },
  ]);

  // Form tạo / cập nhật danh mục
  const [editingCategoryId, setEditingCategoryId] = useState<string | null>(null);
  const [catName, setCatName] = useState("");
  const [catCode, setCatCode] = useState("");
  const [catParentId, setCatParentId] = useState("");

  // Form tạo thương hiệu mới
  const [brandName, setBrandName] = useState("");
  const [brandCode, setBrandCode] = useState("");

  // Form cập nhật giá & trạng thái biến thể SKU (BE2-06 & A10)
  const [editingSku, setEditingSku] = useState<Sku | null>(null);
  const [editRetailPrice, setEditRetailPrice] = useState<number>(0);
  const [editWholesalePrice, setEditWholesalePrice] = useState<number>(0);

  // Trạng thái thông báo
  const [formError, setFormError] = useState<string | null>(null);
  const [formSuccess, setFormSuccess] = useState<string | null>(null);

  const loadData = async () => {
    setLoading(true);
    setLoadError(null);
    const [pRes, cRes, bRes] = await Promise.all([
      apiAdapter.getSkus(),
      apiAdapter.getCategories(),
      apiAdapter.getBrands(),
    ]);

    // [A09] Báo lỗi kết nối rõ ràng thay vì biến thành mảng rỗng = 0
    if (pRes.error) {
      setLoadError(pRes.error.message);
    }
    if (pRes.data) setProducts(pRes.data);
    if (cRes.data) setCategories(cRes.data);
    if (bRes.data) setBrands(bRes.data);
    setLoading(false);
  };

  useEffect(() => {
    loadData();
  }, []);

  const addVariantRow = () => {
    setVariants([
      ...variants,
      {
        variantName: `Biến thể ${variants.length + 1}`,
        sku: `SKU-${Date.now().toString().slice(-5)}-${variants.length + 1}`,
        barcode: generateEan13(),
        retailPrice: 150000,
        wholesalePrice: 100000,
      },
    ]);
  };

  const removeVariantRow = (index: number) => {
    if (variants.length <= 1) return;
    setVariants(variants.filter((_, i) => i !== index));
  };

  const resetProductForm = () => {
    setProdName("");
    setProdCategory("");
    setProdBrand("");
    setProdDesc("");
    setVariants([
      {
        variantName: "Mặc định",
        sku: `SKU-${Date.now().toString().slice(-5)}`,
        barcode: generateEan13(),
        retailPrice: 150000,
        wholesalePrice: 100000,
      },
    ]);
  };

  const handleSaveProduct = async () => {
    setFormError(null);
    if (!prodName.trim()) {
      setFormError("Vui lòng nhập tên sản phẩm.");
      return;
    }

    const skuCodes = variants.map((v) => v.sku.trim().toUpperCase());
    if (skuCodes.some((code) => !code)) {
      setFormError("Mã SKU của từng biến thể không được để trống.");
      return;
    }
    if (new Set(skuCodes).size !== skuCodes.length) {
      setFormError("Phát hiện trùng lặp mã SKU ngay trong danh sách biến thể đang tạo.");
      return;
    }
    if (variants.some((v) => Number(v.retailPrice) < 0 || Number(v.wholesalePrice) < 0)) {
      setFormError("Giá bán lẻ và giá bán sỉ không được nhỏ hơn 0.");
      return;
    }

    setLoading(true);
    const payload = {
      name: prodName.trim(),
      categoryId: prodCategory || undefined,
      brandId: prodBrand || undefined,
      description: prodDesc,
      variants: variants.map((v) => ({
        sku: v.sku.trim(),
        variantName: v.variantName.trim() || v.sku.trim(),
        barcode: v.barcode.trim(),
        retailPrice: Number(v.retailPrice) || 0,
        wholesalePrice: Number(v.wholesalePrice) || 0,
      })),
    };

    const res = await apiAdapter.createProduct(payload);
    setLoading(false);

    if (res.error) {
      setFormError(res.error.message || "Tạo sản phẩm thất bại (Kiểm tra trùng mã SKU 409 Conflict).");
    } else {
      setProductModal(false);
      resetProductForm();
      loadData();
    }
  };

  const handleSaveCategory = async () => {
    setFormError(null);
    setFormSuccess(null);
    if (!catName.trim()) {
      setFormError("Vui lòng nhập tên danh mục.");
      return;
    }

    setLoading(true);
    const res = editingCategoryId
      ? await apiAdapter.updateCategory(editingCategoryId, {
          name: catName.trim(),
          parentId: catParentId || null,
        })
      : await apiAdapter.createCategory({
          name: catName.trim(),
          code: catCode.trim() || undefined,
          parentId: catParentId || null,
        });
    setLoading(false);

    if (res.error) {
      setFormError(res.error.message);
    } else {
      setFormSuccess(editingCategoryId ? "Đã cập nhật danh mục thành công." : "Đã tạo danh mục mới thành công.");
      setEditingCategoryId(null);
      setCatName("");
      setCatCode("");
      setCatParentId("");
      loadData();
    }
  };

  const handleSaveBrand = async () => {
    setFormError(null);
    setFormSuccess(null);
    if (!brandName.trim()) {
      setFormError("Vui lòng nhập tên thương hiệu.");
      return;
    }

    setLoading(true);
    const res = await apiAdapter.createBrand({
      name: brandName.trim(),
      code: brandCode.trim() || undefined,
    });
    setLoading(false);

    if (res.error) {
      setFormError(res.error.message);
    } else {
      setFormSuccess("Đã thêm thương hiệu mới thành công.");
      setBrandName("");
      setBrandCode("");
      loadData();
    }
  };

  const filtered = useMemo(
    () =>
      products.filter((sku) => {
        const matchesQuery = `${sku.name} ${sku.code} ${sku.barcode || ""} ${sku.variant || ""}`
          .toLowerCase()
          .includes(query.toLowerCase());
        const matchesCat = !categoryFilter || sku.categoryId === categoryFilter;
        const matchesBrand = !brandFilter || sku.brandId === brandFilter;
        const isActive = sku.isActive !== false;
        const matchesActive =
          activeFilter === "all" ||
          (activeFilter === "active" && isActive) ||
          (activeFilter === "inactive" && !isActive);
        return matchesQuery && matchesCat && matchesBrand && matchesActive;
      }),
    [products, query, categoryFilter, brandFilter, activeFilter]
  );

  // [A05] Tính toán dữ liệu phân trang
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const pagedProducts = useMemo(() => {
    const safePage = Math.min(page, totalPages);
    const start = (safePage - 1) * pageSize;
    return filtered.slice(start, start + pageSize);
  }, [filtered, page, pageSize, totalPages]);

  return (
    <>
      <PageTitle
        eyebrow="FR-PROD"
        title="Sản phẩm & biến thể"
        description={
          user?.role === "Staff"
            ? "Tra cứu catalog, quản lý danh mục và tạo sản phẩm SKU mới trong phạm vi được phân công."
            : "Quản lý catalog, phân cấp danh mục, thương hiệu, mã vạch và bảng giá bán."
        }
        actions={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                setFormError(null);
                setFormSuccess(null);
                setEditingCategoryId(null);
                setCatName("");
                setCatCode("");
                setCatParentId("");
                setCategoryModal(true);
              }}
            >
              <FolderTree size={17} />Danh mục ({categories.length})
            </Button>
            <Button
              variant="secondary"
              onClick={() => {
                setFormError(null);
                setFormSuccess(null);
                setBrandName("");
                setBrandCode("");
                setBrandModal(true);
              }}
            >
              <Tag size={17} />Thương hiệu ({brands.length})
            </Button>
            {(user?.role === "Owner" || user?.role === "Staff") && (
              <Button
                onClick={() => {
                  setFormError(null);
                  setProductModal(true);
                }}
              >
                <Plus size={17} />Thêm sản phẩm
              </Button>
            )}
          </>
        }
      />

      {/* [A09] Báo lỗi kết nối API và nút Tải lại trang */}
      {loadError && (
        <div
          style={{
            background: "#fef2f2",
            border: "1px solid #ef4444",
            color: "#b91c1c",
            padding: "14px 18px",
            borderRadius: "10px",
            marginBottom: "18px",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "12px",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
            <AlertCircle size={20} />
            <div>
              <strong>Lỗi tải danh sách sản phẩm từ máy chủ:</strong>
              <div style={{ fontSize: "13px" }}>{loadError}</div>
            </div>
          </div>
          <Button variant="secondary" onClick={loadData}>
            <RefreshCw size={15} /> Tải lại trang
          </Button>
        </div>
      )}

      <section className="card">
        <div className="toolbar">
          <label className="search-field">
            <Search size={18} />
            <Input
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setPage(1);
              }}
              placeholder="Tìm tên, SKU hoặc barcode..."
            />
          </label>
          <Select
            aria-label="Danh mục"
            value={categoryFilter}
            onChange={(e) => {
              setCategoryFilter(e.target.value);
              setPage(1);
            }}
          >
            <option value="">Tất cả danh mục ({categories.length})</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
          <Select
            aria-label="Thương hiệu"
            value={brandFilter}
            onChange={(e) => {
              setBrandFilter(e.target.value);
              setPage(1);
            }}
          >
            <option value="">Tất cả thương hiệu ({brands.length})</option>
            {brands.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </Select>
          <Select
            aria-label="Trạng thái hoạt động"
            value={activeFilter}
            onChange={(e) => {
              setActiveFilter(e.target.value as "all" | "active" | "inactive");
              setPage(1);
            }}
          >
            <option value="all">Tất cả trạng thái</option>
            <option value="active">Đang hoạt động</option>
            <option value="inactive">[Ngừng hoạt động]</option>
          </Select>
          {query || categoryFilter || brandFilter || activeFilter !== "all" ? (
            <Button
              variant="secondary"
              onClick={() => {
                setQuery("");
                setCategoryFilter("");
                setBrandFilter("");
                setActiveFilter("all");
                setPage(1);
              }}
            >
              Xóa bộ lọc
            </Button>
          ) : (
            <Button variant="secondary" onClick={loadData}>
              <Filter size={17} />Làm mới
            </Button>
          )}
        </div>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Sản phẩm / SKU</th>
                <th>Mã Barcode</th>
                <th>Giá bán lẻ</th>
                <th>Tồn khả dụng (Theo chi nhánh)</th>
                <th>Trạng thái</th>
                {(user?.role === "Owner" || user?.role === "Staff") && <th></th>}
              </tr>
            </thead>
            <tbody>
              {loading && products.length === 0 ? (
                <tr>
                  <td
                    colSpan={user?.role === "Owner" || user?.role === "Staff" ? 6 : 5}
                    style={{ textAlign: "center", padding: "32px 0", color: "#64748b" }}
                  >
                    <div
                      style={{
                        display: "inline-block",
                        width: "20px",
                        height: "20px",
                        border: "2px solid #cbd5e1",
                        borderTopColor: "#1769e0",
                        borderRadius: "50%",
                        animation: "spin 0.8s linear infinite",
                        verticalAlign: "middle",
                        marginRight: "8px",
                      }}
                    />
                    Đang tải danh mục sản phẩm & biến thể SKU từ máy chủ...
                  </td>
                </tr>
              ) : loadError && products.length === 0 ? (
                <tr>
                  <td
                    colSpan={user?.role === "Owner" || user?.role === "Staff" ? 6 : 5}
                    style={{ textAlign: "center", padding: "36px 0", color: "#b91c1c" }}
                  >
                    Không thể tải danh sách sản phẩm do lỗi kết nối máy chủ.{" "}
                    <Button variant="secondary" onClick={loadData}>
                      Tải lại trang
                    </Button>
                  </td>
                </tr>
              ) : pagedProducts.length === 0 ? (
                <tr>
                  <td
                    colSpan={user?.role === "Owner" || user?.role === "Staff" ? 6 : 5}
                    style={{ textAlign: "center", padding: "36px 0", color: "#64748b" }}
                  >
                    {products.length === 0
                      ? 'Chưa có sản phẩm nào trong cửa hàng. Bấm "Thêm sản phẩm" ở góc phải để khởi tạo SKU đầu tiên.'
                      : "Không tìm thấy sản phẩm / SKU nào khớp với bộ lọc hiện tại."}
                  </td>
                </tr>
              ) : (
                pagedProducts.map((sku) => {
                  const available = sku.onHand - sku.reserved;
                  const isLow = available <= sku.threshold;
                  const isInactive = sku.isActive === false;
                  return (
                    <tr key={sku.id} style={isInactive ? { opacity: 0.72 } : undefined}>
                      <td>
                        <div className="product-cell">
                          <span className="product-thumb">{sku.image || "📦"}</span>
                          <div>
                            <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                              <strong>{sku.name}</strong>
                              {/* [A10] Hiển thị Badge xám [Ngừng hoạt động] cho SKU có isActive === false */}
                              {isInactive && <Badge tone="neutral">[Ngừng hoạt động]</Badge>}
                            </div>
                            <span>
                              {sku.variant ? `${sku.variant} · ` : ""}
                              {sku.code}
                            </span>
                          </div>
                        </div>
                      </td>
                      <td>
                        <code>{sku.barcode || "N/A"}</code>
                      </td>
                      <td>
                        <strong>{sku.sellingPrice.toLocaleString("vi-VN")} ₫</strong>
                      </td>
                      <td>
                        <strong className={isLow ? "text-warning" : ""}>{available}</strong>
                        <span className="muted"> / {sku.onHand} thực tế</span>
                        {sku.branchBalances && sku.branchBalances.length > 0 && (
                          <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                            {sku.branchBalances
                              .map((b) => `${b.branchName}: KD ${b.available} (WAC ${b.averageCost.toLocaleString("vi-VN")}₫)`)
                              .join(" · ")}
                          </div>
                        )}
                      </td>
                      <td>
                        {isInactive ? (
                          <Badge tone="neutral">[Ngừng hoạt động]</Badge>
                        ) : (
                          <Badge tone={isLow ? "warning" : "success"}>
                            {isLow ? "Sắp hết hàng" : "Đang bán"}
                          </Badge>
                        )}
                      </td>
                      {(user?.role === "Owner" || user?.role === "Staff") && (
                        <td>
                          <Button
                            variant="ghost"
                            title="Cập nhật giá bán lẻ / giá sỉ & trạng thái SKU"
                            onClick={() => {
                              setFormError(null);
                              setFormSuccess(null);
                              setEditingSku(sku);
                              setEditRetailPrice(sku.sellingPrice);
                              setEditWholesalePrice(sku.wholesalePrice);
                            }}
                          >
                            <MoreHorizontal size={19} />
                          </Button>
                        </td>
                      )}
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* [A05] Thanh phân trang thực tế */}
        <div className="pagination">
          <span>
            Hiển thị {filtered.length > 0 ? (page - 1) * pageSize + 1 : 0}–
            {Math.min(page * pageSize, filtered.length)} trong tổng số <strong>{filtered.length}</strong> SKU
          </span>
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <Select
              aria-label="Số dòng mỗi trang"
              value={String(pageSize)}
              onChange={(e) => {
                setPageSize(Number(e.target.value));
                setPage(1);
              }}
            >
              <option value="10">10 dòng/trang</option>
              <option value="20">20 dòng/trang</option>
              <option value="50">50 dòng/trang</option>
              <option value="100">100 dòng/trang</option>
            </Select>
            <Button
              variant="secondary"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              <ChevronLeft size={16} /> Trang trước
            </Button>
            <span style={{ fontSize: "13px", fontWeight: 600, padding: "0 6px" }}>
              Trang {page} / {totalPages}
            </span>
            <Button
              variant="secondary"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              Trang sau <ChevronRight size={16} />
            </Button>
          </div>
        </div>
      </section>

      {/* MODAL 1: THÊM SẢN PHẨM & BIẾN THỂ SKU ĐA DÒNG */}
      {productModal && (
        <Modal title="Thêm sản phẩm mới kèm biến thể SKU" onClose={() => setProductModal(false)}>
          {formError && (
            <div
              style={{
                background: "#fee2e2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
                display: "flex",
                alignItems: "center",
                gap: "8px",
              }}
            >
              <AlertCircle size={16} />
              <span>{formError}</span>
            </div>
          )}

          <div className="form-grid">
            <label>
              <span>Tên sản phẩm *</span>
              <Input
                placeholder="Ví dụ: Áo polo thể thao nam OISM"
                value={prodName}
                onChange={(e) => setProdName(e.target.value)}
                required
              />
            </label>
            <label>
              <span>Danh mục</span>
              <Select value={prodCategory} onChange={(e) => setProdCategory(e.target.value)}>
                <option value="">Chọn danh mục</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Thương hiệu</span>
              <Select value={prodBrand} onChange={(e) => setProdBrand(e.target.value)}>
                <option value="">Chọn thương hiệu</option>
                {brands.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Mô tả ngắn</span>
              <Input
                placeholder="Chất liệu cotton co giãn thoáng mát..."
                value={prodDesc}
                onChange={(e) => setProdDesc(e.target.value)}
              />
            </label>
          </div>

          <div style={{ marginTop: "18px", borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "10px",
              }}
            >
              <strong>Danh sách Biến thể SKU ({variants.length})</strong>
              <Button variant="secondary" onClick={addVariantRow}>
                <Plus size={15} />Thêm biến thể
              </Button>
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "10px",
                maxHeight: "240px",
                overflowY: "auto",
              }}
            >
              {variants.map((v, idx) => (
                <div
                  key={idx}
                  style={{
                    background: "#f8fafc",
                    border: "1px solid #e2e8f0",
                    borderRadius: "8px",
                    padding: "10px",
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1.2fr 1.5fr 1fr 1fr auto",
                    gap: "8px",
                    alignItems: "end",
                  }}
                >
                  <label>
                    <span style={{ fontSize: "11px" }}>Biến thể</span>
                    <Input
                      value={v.variantName}
                      onChange={(e) => {
                        const next = [...variants];
                        next[idx].variantName = e.target.value;
                        setVariants(next);
                      }}
                      placeholder="Size M / Đen"
                    />
                  </label>
                  <label>
                    <span style={{ fontSize: "11px" }}>Mã SKU *</span>
                    <Input
                      value={v.sku}
                      onChange={(e) => {
                        const next = [...variants];
                        next[idx].sku = e.target.value;
                        setVariants(next);
                      }}
                      placeholder="SKU-001"
                    />
                  </label>
                  <label>
                    <span style={{ fontSize: "11px" }}>Barcode (EAN-13)</span>
                    <div className="inline-input">
                      <Input
                        value={v.barcode}
                        onChange={(e) => {
                          const next = [...variants];
                          next[idx].barcode = e.target.value;
                          setVariants(next);
                        }}
                        placeholder="893..."
                      />
                      <Button
                        variant="secondary"
                        title="Sinh mã EAN-13"
                        onClick={() => {
                          const next = [...variants];
                          next[idx].barcode = generateEan13();
                          setVariants(next);
                        }}
                      >
                        <Barcode size={15} />
                      </Button>
                    </div>
                  </label>
                  <label>
                    <span style={{ fontSize: "11px" }}>Giá lẻ (VNĐ)</span>
                    <Input
                      type="number"
                      min="0"
                      value={v.retailPrice}
                      onChange={(e) => {
                        const next = [...variants];
                        next[idx].retailPrice = Number(e.target.value);
                        setVariants(next);
                      }}
                    />
                  </label>
                  <label>
                    <span style={{ fontSize: "11px" }}>Giá sỉ (VNĐ)</span>
                    <Input
                      type="number"
                      min="0"
                      value={v.wholesalePrice}
                      onChange={(e) => {
                        const next = [...variants];
                        next[idx].wholesalePrice = Number(e.target.value);
                        setVariants(next);
                      }}
                    />
                  </label>
                  <Button
                    variant="ghost"
                    disabled={variants.length <= 1}
                    onClick={() => removeVariantRow(idx)}
                  >
                    <Trash2 size={16} color="#ef4444" />
                  </Button>
                </div>
              ))}
            </div>
          </div>

          <div className="hint-box" style={{ marginTop: "14px" }}>
            Mã SKU và Barcode phải là duy nhất trong cửa hàng. Giá vốn (COGS) sẽ được tự động tính qua các Phiếu nhập kho (WAC) và không gán cố định tại đây.
          </div>

          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setProductModal(false)}>
              Hủy
            </Button>
            <Button onClick={handleSaveProduct} disabled={loading}>
              {loading ? "Đang lưu..." : "Lưu sản phẩm"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL 2: QUẢN LÝ CÂY DANH MỤC */}
      {categoryModal && (
        <Modal
          title="Quản lý Danh mục hàng hóa (Phân cấp cha - con)"
          onClose={() => setCategoryModal(false)}
        >
          {formError && (
            <div
              style={{
                background: "#fee2e2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
                display: "flex",
                alignItems: "center",
                gap: "8px",
              }}
            >
              <AlertCircle size={16} />
              <span>{formError}</span>
            </div>
          )}
          {formSuccess && (
            <div
              style={{
                background: "#dcfce7",
                border: "1px solid #22c55e",
                color: "#15803d",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              {formSuccess}
            </div>
          )}

          <div className="form-grid">
            <label>
              <span>{editingCategoryId ? "Đổi tên danh mục *" : "Tên danh mục mới *"}</span>
              <Input
                placeholder="Ví dụ: Áo nam"
                value={catName}
                onChange={(e) => setCatName(e.target.value)}
                required
              />
            </label>
            <label>
              <span>Mã danh mục</span>
              <Input
                placeholder="CAT-AO-NAM"
                value={catCode}
                onChange={(e) => setCatCode(e.target.value)}
                disabled={Boolean(editingCategoryId)}
              />
            </label>
            <label style={{ gridColumn: "1 / -1" }}>
              <span>Danh mục cha (để trống nếu là danh mục gốc)</span>
              <Select value={catParentId} onChange={(e) => setCatParentId(e.target.value)}>
                <option value="">-- Là danh mục gốc (Level 1) --</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name} {c.id === editingCategoryId ? "(Chính danh mục này)" : ""}
                  </option>
                ))}
              </Select>
            </label>
          </div>

          {categories.length > 0 && (
            <div style={{ marginTop: "14px", borderTop: "1px solid #e2e8f0", paddingTop: "10px" }}>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "6px",
                }}
              >
                <strong style={{ fontSize: "12px", color: "#475569" }}>
                  Cây danh mục hiện có ({categories.length}) — Bấm "Sửa" để đổi quan hệ cha-con:
                </strong>
                {editingCategoryId && (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingCategoryId(null);
                      setCatName("");
                      setCatCode("");
                      setCatParentId("");
                      setFormError(null);
                    }}
                    style={{
                      background: "none",
                      border: "none",
                      color: "#1769e0",
                      fontSize: "12px",
                      cursor: "pointer",
                      fontWeight: 600,
                    }}
                  >
                    + Chuyển về chế độ Tạo mới
                  </button>
                )}
              </div>
              <div
                style={{
                  maxHeight: "150px",
                  overflowY: "auto",
                  border: "1px solid #e2e8f0",
                  borderRadius: "8px",
                  padding: "6px 10px",
                  background: "#f8fafc",
                }}
              >
                {categories.map((c) => (
                  <div
                    key={c.id}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "5px 0",
                      borderBottom: "1px dashed #e2e8f0",
                      fontSize: "13px",
                    }}
                  >
                    <span>
                      <strong>{c.name}</strong>{" "}
                      {c.code ? (
                        <code style={{ fontSize: "11px", color: "#64748b" }}>({c.code})</code>
                      ) : null}
                    </span>
                    <button
                      type="button"
                      onClick={() => {
                        setFormError(null);
                        setFormSuccess(null);
                        setEditingCategoryId(c.id);
                        setCatName(c.rawName || c.name.replace(/^[—\s]+/, ""));
                        setCatCode(c.code || "");
                        setCatParentId(c.parentId || "");
                      }}
                      style={{
                        background: editingCategoryId === c.id ? "#dbeafe" : "#fff",
                        border: "1px solid #cbd5e1",
                        borderRadius: "6px",
                        padding: "2px 8px",
                        fontSize: "11px",
                        cursor: "pointer",
                        color: "#1e293b",
                      }}
                    >
                      {editingCategoryId === c.id ? "Đang chọn sửa" : "Sửa cha/tên"}
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div className="hint-box" style={{ marginTop: "12px" }}>
            Hệ thống tự động kiểm soát chống lặp quan hệ cha con đệ quy (mã lỗi 400 Bad Request khi gán cha vào chính nó hoặc danh mục con của nó).
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setCategoryModal(false)}>
              Đóng
            </Button>
            <Button onClick={handleSaveCategory} disabled={loading}>
              {loading ? "Đang xử lý..." : editingCategoryId ? "Cập nhật danh mục" : "Thêm danh mục"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL 3: QUẢN LÝ THƯƠNG HIỆU */}
      {brandModal && (
        <Modal title="Quản lý Thương hiệu sản phẩm" onClose={() => setBrandModal(false)}>
          {formError && (
            <div
              style={{
                background: "#fee2e2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              {formError}
            </div>
          )}
          {formSuccess && (
            <div
              style={{
                background: "#dcfce7",
                border: "1px solid #22c55e",
                color: "#15803d",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              {formSuccess}
            </div>
          )}
          <div className="form-grid">
            <label>
              <span>Tên thương hiệu *</span>
              <Input
                placeholder="Ví dụ: OISM Original"
                value={brandName}
                onChange={(e) => setBrandName(e.target.value)}
                required
              />
            </label>
            <label>
              <span>Mã thương hiệu</span>
              <Input
                placeholder="BRAND-OISM"
                value={brandCode}
                onChange={(e) => setBrandCode(e.target.value)}
              />
            </label>
          </div>
          {brands.length > 0 && (
            <div style={{ marginTop: "14px", borderTop: "1px solid #e2e8f0", paddingTop: "10px" }}>
              <strong
                style={{ fontSize: "12px", color: "#475569", display: "block", marginBottom: "6px" }}
              >
                Thương hiệu hiện có ({brands.length}):
              </strong>
              <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", maxHeight: "120px", overflowY: "auto" }}>
                {brands.map((b) => (
                  <span
                    key={b.id}
                    style={{
                      background: "#f1f5f9",
                      border: "1px solid #cbd5e1",
                      borderRadius: "6px",
                      padding: "4px 8px",
                      fontSize: "12px",
                    }}
                  >
                    <strong>{b.name}</strong> {b.code ? `(${b.code})` : ""}
                  </span>
                ))}
              </div>
            </div>
          )}
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setBrandModal(false)}>
              Đóng
            </Button>
            <Button onClick={handleSaveBrand} disabled={loading}>
              {loading ? "Đang tạo..." : "Thêm thương hiệu"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL 4: CẬP NHẬT BẢNG GIÁ & TRẠNG THÁI BIẾN THỂ SKU (BE2-06 & A10) */}
      {editingSku && (
        <Modal
          title={`Quản lý SKU: ${editingSku.code}`}
          onClose={() => setEditingSku(null)}
        >
          {formError && (
            <div
              style={{
                background: "#fee2e2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              {formError}
            </div>
          )}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
            <div>
              <strong>Trạng thái kinh doanh hiện tại:</strong>{" "}
              {editingSku.isActive === false ? (
                <Badge tone="neutral">[Ngừng hoạt động]</Badge>
              ) : (
                <Badge tone="success">Đang hoạt động</Badge>
              )}
            </div>
            <Button
              variant="secondary"
              disabled={loading}
              onClick={async () => {
                setFormError(null);
                setLoading(true);
                const res = await apiAdapter.toggleVariantStatus(editingSku.productId, editingSku.id);
                setLoading(false);
                if (res.error) {
                  setFormError(res.error.message);
                } else {
                  setEditingSku(null);
                  loadData();
                }
              }}
            >
              <Power size={15} />
              {editingSku.isActive === false ? "Kích hoạt lại SKU" : "Ngừng hoạt động SKU"}
            </Button>
          </div>

          {user?.role === "Owner" && (
            <div className="form-grid">
              <label>
                <span>Giá bán lẻ (Retail Price - VNĐ) *</span>
                <Input
                  type="number"
                  min="0"
                  value={editRetailPrice}
                  onChange={(e) => setEditRetailPrice(Number(e.target.value))}
                />
              </label>
              <label>
                <span>Giá bán sỉ (Wholesale Price - VNĐ) *</span>
                <Input
                  type="number"
                  min="0"
                  value={editWholesalePrice}
                  onChange={(e) => setEditWholesalePrice(Number(e.target.value))}
                />
              </label>
            </div>
          )}
          <div className="hint-box" style={{ marginTop: "12px" }}>
            Giá vốn bình quân hiện tại (WAC):{" "}
            <strong>{editingSku.averageCost.toLocaleString("vi-VN")} ₫</strong>. Khi SKU ở trạng thái{" "}
            <strong>[Ngừng hoạt động]</strong>, mặt hàng sẽ bị khóa không cho chọn vào đơn hàng hoặc giỏ POS.
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setEditingSku(null)}>
              Hủy
            </Button>
            {user?.role === "Owner" && (
              <Button
                disabled={loading}
                onClick={async () => {
                  setFormError(null);
                  setLoading(true);
                  const res = await apiAdapter.updateVariantPrices(editingSku.productId, editingSku.id, {
                    retailPrice: editRetailPrice,
                    wholesalePrice: editWholesalePrice,
                  });
                  setLoading(false);
                  if (res.error) {
                    setFormError(res.error.message);
                  } else {
                    setEditingSku(null);
                    loadData();
                  }
                }}
              >
                {loading ? "Đang lưu..." : "Lưu bảng giá"}
              </Button>
            )}
          </div>
        </Modal>
      )}
    </>
  );
}
