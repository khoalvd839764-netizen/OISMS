import {
  AlertCircle,
  Barcode,
  Edit3,
  FolderTree,
  Layers,
  PackagePlus,
  Plus,
  RefreshCw,
  Search,
  Tag,
  Trash2,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { Badge, Button, EmptyState, Input, Modal, PageTitle, Select } from '../components/common/ui';
import { TableSkeleton } from '../components/feedback/Skeleton';
import { useToast } from '../components/feedback/Toast';
import { useAuth } from '../context/AuthContext';
import { useDebounce } from '../hooks/useDebounce';
import {
  catalogApi,
  generateEan13Barcode,
  isValidEan13,
} from '../api/catalog.api';
import { parseApiError } from '../api/client';
import type {
  BrandResponse,
  CategoryResponse,
  CreateProductVariantRequest,
  ProductResponse,
} from '../types/catalog.types';

type ActiveTab = 'products' | 'categories' | 'brands';

interface FlatCategoryItem {
  id: string;
  name: string;
  description: string | null;
  parentId: string | null;
  parentName: string | null;
  isActive: boolean;
  depth: number;
  subCount: number;
}

function flattenCategories(
  categories: CategoryResponse[],
  depth = 0,
  parentName: string | null = null
): FlatCategoryItem[] {
  const result: FlatCategoryItem[] = [];
  for (const cat of categories) {
    result.push({
      id: cat.id,
      name: cat.name,
      description: cat.description,
      parentId: cat.parentId,
      parentName,
      isActive: cat.isActive,
      depth,
      subCount: cat.subCategories?.length || 0,
    });
    if (cat.subCategories && cat.subCategories.length > 0) {
      result.push(...flattenCategories(cat.subCategories, depth + 1, cat.name));
    }
  }
  return result;
}

function collectDescendantIds(categories: CategoryResponse[], targetId: string): Set<string> {
  const ids = new Set<string>();
  function traverse(nodes: CategoryResponse[], insideTarget: boolean) {
    for (const node of nodes) {
      const match = insideTarget || node.id === targetId;
      if (match) {
        ids.add(node.id);
      }
      if (node.subCategories?.length) {
        traverse(node.subCategories, match);
      }
    }
  }
  traverse(categories, false);
  return ids;
}

export function ProductsPage() {
  const { hasRole } = useAuth();
  const { showToast } = useToast();
  const canManage = hasRole(['Owner', 'Staff']);

  const [activeTab, setActiveTab] = useState<ActiveTab>('products');

  // Data states
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [categories, setCategories] = useState<CategoryResponse[]>([]);
  const [brands, setBrands] = useState<BrandResponse[]>([]);

  // Loading & Error states
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Filters for Products
  const [searchQuery, setSearchQuery] = useState('');
  const debouncedSearch = useDebounce(searchQuery, 300);
  const [selectedCategoryId, setSelectedCategoryId] = useState('');
  const [selectedBrandId, setSelectedBrandId] = useState('');

  // Flatten categories for dropdowns & tree table
  const flatCategories = useMemo(() => flattenCategories(categories), [categories]);

  // Load reference data (categories & brands)
  const loadMetadata = useCallback(async () => {
    try {
      const [catData, brandData] = await Promise.all([
        catalogApi.getCategories(),
        catalogApi.getBrands(),
      ]);
      setCategories(catData);
      setBrands(brandData);
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể tải danh mục / thương hiệu', parsed.message);
    }
  }, [showToast]);

  // Load products with filter
  const loadProducts = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const data = await catalogApi.getProducts({
        search: debouncedSearch.trim() || undefined,
        categoryId: selectedCategoryId || undefined,
        brandId: selectedBrandId || undefined,
      });
      setProducts(data);
    } catch (err) {
      const parsed = parseApiError(err);
      setErrorMsg(parsed.message);
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, selectedCategoryId, selectedBrandId]);

  useEffect(() => {
    void loadMetadata();
  }, [loadMetadata]);

  useEffect(() => {
    void loadProducts();
  }, [loadProducts]);

  // ============================================================================
  // 1. PRODUCT & MULTI-VARIANT MODAL STATE
  // ============================================================================
  const [productModalOpen, setProductModalOpen] = useState(false);
  const [prodName, setProdName] = useState('');
  const [prodDesc, setProdDesc] = useState('');
  const [prodCategoryId, setProdCategoryId] = useState('');
  const [prodBrandId, setProdBrandId] = useState('');
  const [variantsDraft, setVariantsDraft] = useState<CreateProductVariantRequest[]>([
    {
      sku: '',
      variantName: 'Mặc định',
      barcode: '',
      retailPrice: 0,
      wholesalePrice: 0,
      color: '',
      size: '',
    },
  ]);

  // Edit Product Modal State
  const [editingProduct, setEditingProduct] = useState<ProductResponse | null>(null);
  const [editProdName, setEditProdName] = useState('');
  const [editProdDesc, setEditProdDesc] = useState('');
  const [editProdCategoryId, setEditProdCategoryId] = useState('');
  const [editProdBrandId, setEditProdBrandId] = useState('');
  const [editProdActive, setEditProdActive] = useState(true);

  // Add Variant to Existing Product Modal State
  const [addingVariantProduct, setAddingVariantProduct] = useState<ProductResponse | null>(null);
  const [singleVariantDraft, setSingleVariantDraft] = useState<CreateProductVariantRequest>({
    sku: '',
    variantName: '',
    barcode: '',
    retailPrice: 0,
    wholesalePrice: 0,
    color: '',
    size: '',
  });

  const openCreateProductModal = () => {
    setProdName('');
    setProdDesc('');
    setProdCategoryId('');
    setProdBrandId('');
    setVariantsDraft([
      {
        sku: '',
        variantName: 'Tiêu chuẩn',
        barcode: generateEan13Barcode(),
        retailPrice: 0,
        wholesalePrice: 0,
        color: '',
        size: '',
      },
    ]);
    setProductModalOpen(true);
  };

  const addVariantRow = () => {
    setVariantsDraft((prev) => [
      ...prev,
      {
        sku: '',
        variantName: '',
        barcode: generateEan13Barcode(),
        retailPrice: prev[0]?.retailPrice || 0,
        wholesalePrice: prev[0]?.wholesalePrice || 0,
        color: '',
        size: '',
      },
    ]);
  };

  const removeVariantRow = (idx: number) => {
    if (variantsDraft.length <= 1) return;
    setVariantsDraft((prev) => prev.filter((_, i) => i !== idx));
  };

  const updateVariantRow = <K extends keyof CreateProductVariantRequest>(
    idx: number,
    key: K,
    value: CreateProductVariantRequest[K]
  ) => {
    setVariantsDraft((prev) =>
      prev.map((row, i) => (i === idx ? { ...row, [key]: value } : row))
    );
  };

  const handleCreateProductSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!prodName.trim()) {
      showToast('warning', 'Thiếu thông tin', 'Vui lòng nhập tên sản phẩm.');
      return;
    }

    for (let i = 0; i < variantsDraft.length; i++) {
      const v = variantsDraft[i];
      if (!v.sku.trim() || !v.variantName.trim()) {
        showToast('warning', 'Thiếu thông tin SKU', `Dòng biến thể #${i + 1} cần có Mã SKU và Tên biến thể.`);
        return;
      }
      if (v.retailPrice < 0 || v.wholesalePrice < 0) {
        showToast('warning', 'Giá không hợp lệ', `Giá bán tại dòng #${i + 1} không được âm.`);
        return;
      }
      if (v.barcode && v.barcode.trim().length === 13 && !isValidEan13(v.barcode.trim())) {
        showToast('warning', 'Sai chuẩn EAN-13', `Barcode "${v.barcode}" ở dòng #${i + 1} không đúng checksum EAN-13.`);
        return;
      }
    }

    setSubmitting(true);
    try {
      await catalogApi.createProduct({
        name: prodName.trim(),
        description: prodDesc.trim() || null,
        categoryId: prodCategoryId || null,
        brandId: prodBrandId || null,
        variants: variantsDraft.map((v) => ({
          sku: v.sku.trim().toUpperCase(),
          variantName: v.variantName.trim(),
          barcode: v.barcode?.trim() || null,
          retailPrice: Number(v.retailPrice),
          wholesalePrice: Number(v.wholesalePrice),
          color: v.color?.trim() || null,
          size: v.size?.trim() || null,
        })),
      });
      showToast('success', 'Đã tạo sản phẩm mới', `Sản phẩm "${prodName}" cùng ${variantsDraft.length} SKU đã được lưu.`);
      setProductModalOpen(false);
      await loadProducts();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi tạo sản phẩm', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const openEditProduct = (product: ProductResponse) => {
    setEditingProduct(product);
    setEditProdName(product.name);
    setEditProdDesc(product.description || '');
    setEditProdCategoryId(product.categoryId || '');
    setEditProdBrandId(product.brandId || '');
    setEditProdActive(product.isActive);
  };

  const handleUpdateProductSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!editingProduct || !editProdName.trim()) return;

    setSubmitting(true);
    try {
      await catalogApi.updateProduct(editingProduct.id, {
        name: editProdName.trim(),
        description: editProdDesc.trim() || null,
        categoryId: editProdCategoryId || null,
        brandId: editProdBrandId || null,
        isActive: editProdActive,
      });
      showToast('success', 'Đã cập nhật sản phẩm', `Thông tin "${editProdName}" đã được lưu.`);
      setEditingProduct(null);
      await loadProducts();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể cập nhật sản phẩm', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeactivateProduct = async (product: ProductResponse) => {
    if (!window.confirm(`Bạn có chắc muốn ngừng kinh doanh sản phẩm "${product.name}"?`)) return;
    try {
      await catalogApi.deleteProduct(product.id);
      showToast('info', 'Đã ngừng kinh doanh', `Sản phẩm "${product.name}" đã chuyển sang trạng thái ngừng bán.`);
      await loadProducts();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi thao tác', parsed.message);
    }
  };

  const openAddVariantModal = (product: ProductResponse) => {
    setAddingVariantProduct(product);
    setSingleVariantDraft({
      sku: '',
      variantName: '',
      barcode: generateEan13Barcode(),
      retailPrice: product.variants[0]?.retailPrice || 0,
      wholesalePrice: product.variants[0]?.wholesalePrice || 0,
      color: '',
      size: '',
    });
  };

  const handleAddVariantSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!addingVariantProduct) return;
    if (!singleVariantDraft.sku.trim() || !singleVariantDraft.variantName.trim()) {
      showToast('warning', 'Thiếu thông tin', 'Vui lòng nhập Mã SKU và Tên biến thể.');
      return;
    }

    setSubmitting(true);
    try {
      await catalogApi.addVariant(addingVariantProduct.id, {
        sku: singleVariantDraft.sku.trim().toUpperCase(),
        variantName: singleVariantDraft.variantName.trim(),
        barcode: singleVariantDraft.barcode?.trim() || null,
        retailPrice: Number(singleVariantDraft.retailPrice),
        wholesalePrice: Number(singleVariantDraft.wholesalePrice),
        color: singleVariantDraft.color?.trim() || null,
        size: singleVariantDraft.size?.trim() || null,
      });
      showToast('success', 'Đã thêm biến thể SKU', `Đã gắn SKU "${singleVariantDraft.sku}" vào "${addingVariantProduct.name}".`);
      setAddingVariantProduct(null);
      await loadProducts();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể thêm biến thể', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  // ============================================================================
  // 2. CATEGORY CRUD MODAL STATE
  // ============================================================================
  const [categoryModalOpen, setCategoryModalOpen] = useState(false);
  const [editingCategory, setEditingCategory] = useState<FlatCategoryItem | null>(null);
  const [catName, setCatName] = useState('');
  const [catDesc, setCatDesc] = useState('');
  const [catParentId, setCatParentId] = useState('');
  const [catIsActive, setCatIsActive] = useState(true);

  const openCreateCategory = () => {
    setEditingCategory(null);
    setCatName('');
    setCatDesc('');
    setCatParentId('');
    setCatIsActive(true);
    setCategoryModalOpen(true);
  };

  const openEditCategory = (cat: FlatCategoryItem) => {
    setEditingCategory(cat);
    setCatName(cat.name);
    setCatDesc(cat.description || '');
    setCatParentId(cat.parentId || '');
    setCatIsActive(cat.isActive);
    setCategoryModalOpen(true);
  };

  const forbiddenParentIds = useMemo(() => {
    if (!editingCategory) return new Set<string>();
    return collectDescendantIds(categories, editingCategory.id);
  }, [categories, editingCategory]);

  const handleCategorySubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!catName.trim()) {
      showToast('warning', 'Thiếu tên danh mục', 'Vui lòng nhập tên danh mục.');
      return;
    }

    setSubmitting(true);
    try {
      if (editingCategory) {
        await catalogApi.updateCategory(editingCategory.id, {
          name: catName.trim(),
          description: catDesc.trim() || null,
          parentId: catParentId || null,
          isActive: catIsActive,
        });
        showToast('success', 'Đã cập nhật danh mục', `Danh mục "${catName}" đã được cập nhật.`);
      } else {
        await catalogApi.createCategory({
          name: catName.trim(),
          description: catDesc.trim() || null,
          parentId: catParentId || null,
        });
        showToast('success', 'Đã tạo danh mục', `Danh mục "${catName}" đã được thêm vào cây phân loại.`);
      }
      setCategoryModalOpen(false);
      await loadMetadata();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi lưu danh mục', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeactivateCategory = async (cat: FlatCategoryItem) => {
    if (!window.confirm(`Bạn có chắc muốn vô hiệu hóa danh mục "${cat.name}"?`)) return;
    try {
      await catalogApi.deleteCategory(cat.id);
      showToast('info', 'Đã vô hiệu hóa danh mục', `Danh mục "${cat.name}" đã ngừng hoạt động.`);
      await loadMetadata();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể xóa danh mục', parsed.message);
    }
  };

  // ============================================================================
  // 3. BRAND CRUD MODAL STATE
  // ============================================================================
  const [brandModalOpen, setBrandModalOpen] = useState(false);
  const [editingBrand, setEditingBrand] = useState<BrandResponse | null>(null);
  const [brandName, setBrandName] = useState('');
  const [brandDesc, setBrandDesc] = useState('');
  const [brandIsActive, setBrandIsActive] = useState(true);

  const openCreateBrand = () => {
    setEditingBrand(null);
    setBrandName('');
    setBrandDesc('');
    setBrandIsActive(true);
    setBrandModalOpen(true);
  };

  const openEditBrand = (b: BrandResponse) => {
    setEditingBrand(b);
    setBrandName(b.name);
    setBrandDesc(b.description || '');
    setBrandIsActive(b.isActive);
    setBrandModalOpen(true);
  };

  const handleBrandSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!brandName.trim()) {
      showToast('warning', 'Thiếu tên thương hiệu', 'Vui lòng nhập tên thương hiệu.');
      return;
    }

    setSubmitting(true);
    try {
      if (editingBrand) {
        await catalogApi.updateBrand(editingBrand.id, {
          name: brandName.trim(),
          description: brandDesc.trim() || null,
          isActive: brandIsActive,
        });
        showToast('success', 'Đã cập nhật thương hiệu', `Thương hiệu "${brandName}" đã được lưu.`);
      } else {
        await catalogApi.createBrand({
          name: brandName.trim(),
          description: brandDesc.trim() || null,
        });
        showToast('success', 'Đã thêm thương hiệu', `Thương hiệu "${brandName}" đã sẵn sàng sử dụng.`);
      }
      setBrandModalOpen(false);
      await loadMetadata();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Lỗi lưu thương hiệu', parsed.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeactivateBrand = async (b: BrandResponse) => {
    if (!window.confirm(`Bạn có chắc muốn ngừng kích hoạt thương hiệu "${b.name}"?`)) return;
    try {
      await catalogApi.deleteBrand(b.id);
      showToast('info', 'Đã vô hiệu hóa thương hiệu', `Thương hiệu "${b.name}" đã ngừng hoạt động.`);
      await loadMetadata();
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể xóa thương hiệu', parsed.message);
    }
  };

  // Count total SKUs across loaded products
  const totalSkuCount = useMemo(
    () => products.reduce((sum, p) => sum + (p.variants?.length || 0), 0),
    [products]
  );

  return (
    <>
      <PageTitle
        eyebrow="CATALOG & SKU MANAGEMENT"
        title="Sản phẩm, Danh mục & Thương hiệu"
        description="Quản lý cấu trúc cây danh mục cha-con, thương hiệu và biến thể SKU chuẩn mã vạch EAN-13."
        actions={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                void loadMetadata();
                void loadProducts();
              }}
            >
              <RefreshCw size={16} />
              Làm mới
            </Button>
            {canManage && activeTab === 'products' && (
              <Button onClick={openCreateProductModal}>
                <Plus size={17} />
                Thêm sản phẩm & SKU
              </Button>
            )}
            {canManage && activeTab === 'categories' && (
              <Button onClick={openCreateCategory}>
                <Plus size={17} />
                Thêm danh mục
              </Button>
            )}
            {canManage && activeTab === 'brands' && (
              <Button onClick={openCreateBrand}>
                <Plus size={17} />
                Thêm thương hiệu
              </Button>
            )}
          </>
        }
      />

      <div className="segmented" style={{ marginBottom: '18px' }}>
        <button
          type="button"
          className={activeTab === 'products' ? 'active' : ''}
          onClick={() => setActiveTab('products')}
        >
          <Layers size={16} /> Sản phẩm & SKU ({products.length} SP / {totalSkuCount} SKU)
        </button>
        <button
          type="button"
          className={activeTab === 'categories' ? 'active' : ''}
          onClick={() => setActiveTab('categories')}
        >
          <FolderTree size={16} /> Danh mục Cha - Con ({flatCategories.length})
        </button>
        <button
          type="button"
          className={activeTab === 'brands' ? 'active' : ''}
          onClick={() => setActiveTab('brands')}
        >
          <Tag size={16} /> Thương hiệu ({brands.length})
        </button>
      </div>

      {/* =====================================================================
          TAB 1: PRODUCTS & SKU VARIANTS
      ===================================================================== */}
      {activeTab === 'products' && (
        <section className="card">
          <div className="toolbar">
            <label className="search-field">
              <Search size={18} />
              <Input
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Tìm theo tên sản phẩm, mã SKU hoặc Barcode..."
              />
            </label>

            <Select
              aria-label="Lọc theo danh mục"
              value={selectedCategoryId}
              onChange={(e) => setSelectedCategoryId(e.target.value)}
            >
              <option value="">Tất cả danh mục</option>
              {flatCategories.map((cat) => (
                <option key={cat.id} value={cat.id}>
                  {'— '.repeat(cat.depth)}
                  {cat.name}
                </option>
              ))}
            </Select>

            <Select
              aria-label="Lọc theo thương hiệu"
              value={selectedBrandId}
              onChange={(e) => setSelectedBrandId(e.target.value)}
            >
              <option value="">Tất cả thương hiệu</option>
              {brands.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
          </div>

          {errorMsg && (
            <div
              role="alert"
              style={{
                margin: '16px',
                padding: '14px 16px',
                borderRadius: '10px',
                background: '#FEF2F2',
                border: '1px solid #FECACA',
                color: '#991B1B',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
              }}
            >
              <span style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <AlertCircle size={18} />
                {errorMsg}
              </span>
              <Button variant="secondary" onClick={() => void loadProducts()}>
                Thử lại
              </Button>
            </div>
          )}

          {loading ? (
            <div style={{ padding: '16px' }}>
              <TableSkeleton rows={6} columns={6} />
            </div>
          ) : products.length === 0 ? (
            <EmptyState
              title="Chưa có sản phẩm nào phù hợp"
              description="Hãy tạo danh mục, thương hiệu và thêm sản phẩm đầu tiên cùng các biến thể SKU."
              action={
                canManage ? (
                  <Button onClick={openCreateProductModal}>
                    <Plus size={16} /> Tạo sản phẩm đầu tiên
                  </Button>
                ) : undefined
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Sản phẩm / Biến thể SKU</th>
                    <th>Phân loại</th>
                    <th>Mã SKU & Barcode</th>
                    <th>Thuộc tính</th>
                    <th>Giá bán lẻ / Giá sỉ</th>
                    <th>Trạng thái</th>
                    {canManage && <th>Thao tác</th>}
                  </tr>
                </thead>
                <tbody>
                  {products.map((product) =>
                    product.variants && product.variants.length > 0 ? (
                      product.variants.map((variant, vIdx) => (
                        <tr key={variant.id}>
                          <td>
                            <div className="product-cell">
                              <span className="product-thumb">
                                {product.name.slice(0, 2).toUpperCase()}
                              </span>
                              <div>
                                <strong>{product.name}</strong>
                                <span>{variant.variantName}</span>
                              </div>
                            </div>
                          </td>
                          <td>
                            <div>
                              <strong>{product.categoryName || 'Chưa phân loại'}</strong>
                              <div className="muted" style={{ fontSize: '12px' }}>
                                {product.brandName || 'Không thương hiệu'}
                              </div>
                            </div>
                          </td>
                          <td>
                            <span className="mono">{variant.sku}</span>
                            {variant.barcode && (
                              <div className="muted mono" style={{ fontSize: '12px' }}>
                                EAN: {variant.barcode}
                              </div>
                            )}
                          </td>
                          <td>
                            {[variant.color, variant.size].filter(Boolean).join(' / ') || '—'}
                          </td>
                          <td>
                            <strong>{variant.retailPrice.toLocaleString('vi-VN')} ₫</strong>
                            <div className="muted" style={{ fontSize: '12px' }}>
                              Sỉ: {variant.wholesalePrice.toLocaleString('vi-VN')} ₫
                            </div>
                          </td>
                          <td>
                            <Badge tone={product.isActive && variant.isActive ? 'success' : 'neutral'}>
                              {product.isActive && variant.isActive ? 'Đang bán' : 'Ngừng bán'}
                            </Badge>
                          </td>
                          {canManage && (
                            <td>
                              {vIdx === 0 && (
                                <div style={{ display: 'flex', gap: '6px' }}>
                                  <Button
                                    variant="ghost"
                                    title="Thêm biến thể SKU cho sản phẩm này"
                                    onClick={() => openAddVariantModal(product)}
                                  >
                                    <PackagePlus size={16} />
                                  </Button>
                                  <Button
                                    variant="ghost"
                                    title="Sửa thông tin sản phẩm"
                                    onClick={() => openEditProduct(product)}
                                  >
                                    <Edit3 size={16} />
                                  </Button>
                                  {product.isActive && (
                                    <Button
                                      variant="ghost"
                                      title="Ngừng kinh doanh"
                                      onClick={() => void handleDeactivateProduct(product)}
                                    >
                                      <Trash2 size={16} />
                                    </Button>
                                  )}
                                </div>
                              )}
                            </td>
                          )}
                        </tr>
                      ))
                    ) : (
                      <tr key={product.id}>
                        <td>
                          <strong>{product.name}</strong>
                        </td>
                        <td>{product.categoryName || '—'}</td>
                        <td colSpan={3} className="muted">
                          Chưa có biến thể SKU
                        </td>
                        <td>
                          <Badge tone={product.isActive ? 'success' : 'neutral'}>
                            {product.isActive ? 'Đang bán' : 'Ngừng bán'}
                          </Badge>
                        </td>
                        {canManage && (
                          <td>
                            <div style={{ display: 'flex', gap: '6px' }}>
                              <Button variant="ghost" onClick={() => openAddVariantModal(product)}>
                                <PackagePlus size={16} />
                              </Button>
                              <Button variant="ghost" onClick={() => openEditProduct(product)}>
                                <Edit3 size={16} />
                              </Button>
                            </div>
                          </td>
                        )}
                      </tr>
                    )
                  )}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* =====================================================================
          TAB 2: HIERARCHICAL CATEGORIES (PARENT - CHILD)
      ===================================================================== */}
      {activeTab === 'categories' && (
        <section className="card">
          <div className="card-head">
            <div>
              <h3>Cây danh mục sản phẩm (Cha - Con)</h3>
              <p>Hỗ trợ phân cấp đa tầng và tự động chặn vòng lặp khi cập nhật danh mục cha.</p>
            </div>
          </div>

          {flatCategories.length === 0 ? (
            <EmptyState
              title="Chưa có danh mục nào"
              description="Tạo danh mục gốc và danh mục con để phân loại sản phẩm khoa học."
              action={
                canManage ? (
                  <Button onClick={openCreateCategory}>
                    <Plus size={16} /> Tạo danh mục mới
                  </Button>
                ) : undefined
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Tên danh mục (Phân cấp)</th>
                    <th>Danh mục cha</th>
                    <th>Mô tả</th>
                    <th>Danh mục con</th>
                    <th>Trạng thái</th>
                    {canManage && <th>Thao tác</th>}
                  </tr>
                </thead>
                <tbody>
                  {flatCategories.map((cat) => (
                    <tr key={cat.id}>
                      <td>
                        <div style={{ paddingLeft: `${cat.depth * 24}px`, display: 'flex', alignItems: 'center', gap: '8px' }}>
                          {cat.depth > 0 && <span className="muted">↳</span>}
                          <strong>{cat.name}</strong>
                        </div>
                      </td>
                      <td>{cat.parentName || <span className="muted">Danh mục gốc (Root)</span>}</td>
                      <td>{cat.description || '—'}</td>
                      <td>
                        <span className="mono">{cat.subCount}</span>
                      </td>
                      <td>
                        <Badge tone={cat.isActive ? 'success' : 'neutral'}>
                          {cat.isActive ? 'Hoạt động' : 'Đã ẩn'}
                        </Badge>
                      </td>
                      {canManage && (
                        <td>
                          <div style={{ display: 'flex', gap: '6px' }}>
                            <Button variant="ghost" onClick={() => openEditCategory(cat)} title="Chỉnh sửa">
                              <Edit3 size={16} />
                            </Button>
                            {cat.isActive && (
                              <Button
                                variant="ghost"
                                onClick={() => void handleDeactivateCategory(cat)}
                                title="Vô hiệu hóa"
                              >
                                <Trash2 size={16} />
                              </Button>
                            )}
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* =====================================================================
          TAB 3: BRANDS
      ===================================================================== */}
      {activeTab === 'brands' && (
        <section className="card">
          <div className="card-head">
            <div>
              <h3>Danh sách Thương hiệu (Brands)</h3>
              <p>Quản lý các nhãn hàng và nhà sản xuất gắn với sản phẩm.</p>
            </div>
          </div>

          {brands.length === 0 ? (
            <EmptyState
              title="Chưa có thương hiệu nào"
              description="Thêm thương hiệu để lọc và thống kê sản phẩm theo nhãn hàng."
              action={
                canManage ? (
                  <Button onClick={openCreateBrand}>
                    <Plus size={16} /> Thêm thương hiệu
                  </Button>
                ) : undefined
              }
            />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Tên thương hiệu</th>
                    <th>Mô tả</th>
                    <th>Trạng thái</th>
                    {canManage && <th>Thao tác</th>}
                  </tr>
                </thead>
                <tbody>
                  {brands.map((b) => (
                    <tr key={b.id}>
                      <td>
                        <strong>{b.name}</strong>
                      </td>
                      <td>{b.description || '—'}</td>
                      <td>
                        <Badge tone={b.isActive ? 'success' : 'neutral'}>
                          {b.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}
                        </Badge>
                      </td>
                      {canManage && (
                        <td>
                          <div style={{ display: 'flex', gap: '6px' }}>
                            <Button variant="ghost" onClick={() => openEditBrand(b)} title="Chỉnh sửa">
                              <Edit3 size={16} />
                            </Button>
                            {b.isActive && (
                              <Button
                                variant="ghost"
                                onClick={() => void handleDeactivateBrand(b)}
                                title="Ngừng hoạt động"
                              >
                                <Trash2 size={16} />
                              </Button>
                            )}
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* =====================================================================
          MODAL 1: CREATE PRODUCT WITH MULTI-VARIANT SKUS
      ===================================================================== */}
      {productModalOpen && (
        <Modal
          title="Thêm sản phẩm mới & Danh sách biến thể SKU"
          onClose={() => setProductModalOpen(false)}
          wide
        >
          <form onSubmit={handleCreateProductSubmit}>
            <div className="form-grid">
              <label>
                <span>Tên sản phẩm *</span>
                <Input
                  placeholder="Ví dụ: Áo sơ mi Oxford Premium"
                  value={prodName}
                  onChange={(e) => setProdName(e.target.value)}
                  required
                />
              </label>

              <label>
                <span>Danh mục sản phẩm</span>
                <Select value={prodCategoryId} onChange={(e) => setProdCategoryId(e.target.value)}>
                  <option value="">-- Chọn danh mục --</option>
                  {flatCategories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {'— '.repeat(c.depth)}
                      {c.name}
                    </option>
                  ))}
                </Select>
              </label>

              <label>
                <span>Thương hiệu</span>
                <Select value={prodBrandId} onChange={(e) => setProdBrandId(e.target.value)}>
                  <option value="">-- Chọn thương hiệu --</option>
                  {brands.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </Select>
              </label>

              <label>
                <span>Mô tả sản phẩm</span>
                <Input
                  placeholder="Chất liệu, kiểu dáng, ghi chú..."
                  value={prodDesc}
                  onChange={(e) => setProdDesc(e.target.value)}
                />
              </label>
            </div>

            <div style={{ marginTop: '20px', marginBottom: '10px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <strong>Danh sách biến thể SKU ({variantsDraft.length} dòng)</strong>
              <Button type="button" variant="secondary" onClick={addVariantRow}>
                <Plus size={15} /> Thêm dòng biến thể SKU
              </Button>
            </div>

            <div className="table-wrap" style={{ maxHeight: '320px', overflowY: 'auto' }}>
              <table>
                <thead>
                  <tr>
                    <th>Mã SKU *</th>
                    <th>Tên biến thể *</th>
                    <th>Màu / Size</th>
                    <th>Barcode (EAN-13)</th>
                    <th>Giá bán lẻ *</th>
                    <th>Giá bán sỉ</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {variantsDraft.map((row, idx) => (
                    <tr key={idx}>
                      <td>
                        <Input
                          placeholder="VD: OXF-WHT-M"
                          value={row.sku}
                          onChange={(e) => updateVariantRow(idx, 'sku', e.target.value.toUpperCase())}
                          required
                        />
                      </td>
                      <td>
                        <Input
                          placeholder="Trắng / Size M"
                          value={row.variantName}
                          onChange={(e) => updateVariantRow(idx, 'variantName', e.target.value)}
                          required
                        />
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: '4px' }}>
                          <Input
                            placeholder="Màu"
                            value={row.color || ''}
                            onChange={(e) => updateVariantRow(idx, 'color', e.target.value)}
                          />
                          <Input
                            placeholder="Size"
                            value={row.size || ''}
                            onChange={(e) => updateVariantRow(idx, 'size', e.target.value)}
                          />
                        </div>
                      </td>
                      <td>
                        <div className="inline-input">
                          <Input
                            placeholder="13 chữ số EAN-13"
                            value={row.barcode || ''}
                            onChange={(e) => updateVariantRow(idx, 'barcode', e.target.value)}
                          />
                          <Button
                            type="button"
                            variant="secondary"
                            title="Sinh mã EAN-13 chuẩn"
                            onClick={() => updateVariantRow(idx, 'barcode', generateEan13Barcode())}
                          >
                            <Barcode size={15} />
                          </Button>
                        </div>
                      </td>
                      <td>
                        <Input
                          type="number"
                          min={0}
                          value={row.retailPrice}
                          onChange={(e) => updateVariantRow(idx, 'retailPrice', Number(e.target.value))}
                          required
                        />
                      </td>
                      <td>
                        <Input
                          type="number"
                          min={0}
                          value={row.wholesalePrice}
                          onChange={(e) => updateVariantRow(idx, 'wholesalePrice', Number(e.target.value))}
                        />
                      </td>
                      <td>
                        {variantsDraft.length > 1 && (
                          <Button
                            type="button"
                            variant="ghost"
                            onClick={() => removeVariantRow(idx)}
                            title="Xóa dòng"
                          >
                            <Trash2 size={15} />
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="hint-box" style={{ marginTop: '14px' }}>
              Giá vốn bình quân gia quyền (WAC) sẽ tự động được tính khi xác nhận Phiếu nhập kho. Mã SKU và Barcode phải duy nhất trong toàn bộ doanh nghiệp.
            </div>

            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setProductModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Lưu sản phẩm & SKU'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 2: EDIT PRODUCT METADATA
      ===================================================================== */}
      {editingProduct && (
        <Modal title={`Chỉnh sửa sản phẩm: ${editingProduct.name}`} onClose={() => setEditingProduct(null)}>
          <form onSubmit={handleUpdateProductSubmit}>
            <div className="form-grid">
              <label>
                <span>Tên sản phẩm *</span>
                <Input
                  value={editProdName}
                  onChange={(e) => setEditProdName(e.target.value)}
                  required
                />
              </label>

              <label>
                <span>Danh mục</span>
                <Select
                  value={editProdCategoryId}
                  onChange={(e) => setEditProdCategoryId(e.target.value)}
                >
                  <option value="">-- Chưa phân loại --</option>
                  {flatCategories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {'— '.repeat(c.depth)}
                      {c.name}
                    </option>
                  ))}
                </Select>
              </label>

              <label>
                <span>Thương hiệu</span>
                <Select value={editProdBrandId} onChange={(e) => setEditProdBrandId(e.target.value)}>
                  <option value="">-- Không thương hiệu --</option>
                  {brands.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </Select>
              </label>

              <label>
                <span>Trạng thái kinh doanh</span>
                <Select
                  value={editProdActive ? 'active' : 'inactive'}
                  onChange={(e) => setEditProdActive(e.target.value === 'active')}
                >
                  <option value="active">Đang kinh doanh</option>
                  <option value="inactive">Ngừng kinh doanh</option>
                </Select>
              </label>

              <label style={{ gridColumn: '1 / -1' }}>
                <span>Mô tả</span>
                <Input value={editProdDesc} onChange={(e) => setEditProdDesc(e.target.value)} />
              </label>
            </div>

            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setEditingProduct(null)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang cập nhật...' : 'Lưu thay đổi'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 3: ADD VARIANT TO EXISTING PRODUCT
      ===================================================================== */}
      {addingVariantProduct && (
        <Modal
          title={`Thêm biến thể SKU vào: ${addingVariantProduct.name}`}
          onClose={() => setAddingVariantProduct(null)}
        >
          <form onSubmit={handleAddVariantSubmit}>
            <div className="form-grid">
              <label>
                <span>Mã SKU *</span>
                <Input
                  placeholder="VD: OXF-BLK-L"
                  value={singleVariantDraft.sku}
                  onChange={(e) =>
                    setSingleVariantDraft({ ...singleVariantDraft, sku: e.target.value.toUpperCase() })
                  }
                  required
                />
              </label>

              <label>
                <span>Tên biến thể *</span>
                <Input
                  placeholder="Ví dụ: Đen / Size L"
                  value={singleVariantDraft.variantName}
                  onChange={(e) =>
                    setSingleVariantDraft({ ...singleVariantDraft, variantName: e.target.value })
                  }
                  required
                />
              </label>

              <label>
                <span>Màu sắc</span>
                <Input
                  placeholder="Ví dụ: Đen"
                  value={singleVariantDraft.color || ''}
                  onChange={(e) =>
                    setSingleVariantDraft({ ...singleVariantDraft, color: e.target.value })
                  }
                />
              </label>

              <label>
                <span>Kích cỡ (Size)</span>
                <Input
                  placeholder="Ví dụ: L"
                  value={singleVariantDraft.size || ''}
                  onChange={(e) =>
                    setSingleVariantDraft({ ...singleVariantDraft, size: e.target.value })
                  }
                />
              </label>

              <label style={{ gridColumn: '1 / -1' }}>
                <span>Mã vạch Barcode (EAN-13)</span>
                <div className="inline-input">
                  <Input
                    placeholder="13 chữ số EAN-13"
                    value={singleVariantDraft.barcode || ''}
                    onChange={(e) =>
                      setSingleVariantDraft({ ...singleVariantDraft, barcode: e.target.value })
                    }
                  />
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() =>
                      setSingleVariantDraft({
                        ...singleVariantDraft,
                        barcode: generateEan13Barcode(),
                      })
                    }
                  >
                    <Barcode size={16} /> Sinh mã EAN-13
                  </Button>
                </div>
              </label>

              <label>
                <span>Giá bán lẻ (₫) *</span>
                <Input
                  type="number"
                  min={0}
                  value={singleVariantDraft.retailPrice}
                  onChange={(e) =>
                    setSingleVariantDraft({
                      ...singleVariantDraft,
                      retailPrice: Number(e.target.value),
                    })
                  }
                  required
                />
              </label>

              <label>
                <span>Giá bán sỉ (₫)</span>
                <Input
                  type="number"
                  min={0}
                  value={singleVariantDraft.wholesalePrice}
                  onChange={(e) =>
                    setSingleVariantDraft({
                      ...singleVariantDraft,
                      wholesalePrice: Number(e.target.value),
                    })
                  }
                />
              </label>
            </div>

            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setAddingVariantProduct(null)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang thêm...' : 'Thêm SKU'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 4: CREATE / EDIT CATEGORY (HIERARCHICAL)
      ===================================================================== */}
      {categoryModalOpen && (
        <Modal
          title={editingCategory ? `Cập nhật danh mục: ${editingCategory.name}` : 'Thêm danh mục sản phẩm'}
          onClose={() => setCategoryModalOpen(false)}
        >
          <form onSubmit={handleCategorySubmit}>
            <div className="form-grid">
              <label>
                <span>Tên danh mục *</span>
                <Input
                  placeholder="Ví dụ: Áo khoác nam"
                  value={catName}
                  onChange={(e) => setCatName(e.target.value)}
                  required
                />
              </label>

              <label>
                <span>Danh mục cha (Parent Category)</span>
                <Select value={catParentId} onChange={(e) => setCatParentId(e.target.value)}>
                  <option value="">-- Danh mục gốc (Root) --</option>
                  {flatCategories
                    .filter((c) => !forbiddenParentIds.has(c.id))
                    .map((c) => (
                      <option key={c.id} value={c.id}>
                        {'— '.repeat(c.depth)}
                        {c.name}
                      </option>
                    ))}
                </Select>
              </label>

              <label style={{ gridColumn: '1 / -1' }}>
                <span>Mô tả danh mục</span>
                <Input
                  placeholder="Ghi chú phạm vi mặt hàng..."
                  value={catDesc}
                  onChange={(e) => setCatDesc(e.target.value)}
                />
              </label>

              {editingCategory && (
                <label>
                  <span>Trạng thái</span>
                  <Select
                    value={catIsActive ? 'active' : 'inactive'}
                    onChange={(e) => setCatIsActive(e.target.value === 'active')}
                  >
                    <option value="active">Đang hoạt động</option>
                    <option value="inactive">Vô hiệu hóa</option>
                  </Select>
                </label>
              )}
            </div>

            <div className="hint-box">
              Hệ thống tự động loại bỏ chính danh mục đang sửa và các danh mục con của nó khỏi danh sách chọn để ngăn chặn vòng lặp phân cấp (Circular Dependency).
            </div>

            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setCategoryModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Lưu danh mục'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* =====================================================================
          MODAL 5: CREATE / EDIT BRAND
      ===================================================================== */}
      {brandModalOpen && (
        <Modal
          title={editingBrand ? `Cập nhật thương hiệu: ${editingBrand.name}` : 'Thêm thương hiệu mới'}
          onClose={() => setBrandModalOpen(false)}
        >
          <form onSubmit={handleBrandSubmit}>
            <div className="form-grid">
              <label style={{ gridColumn: '1 / -1' }}>
                <span>Tên thương hiệu *</span>
                <Input
                  placeholder="Ví dụ: Coolmate, Uniqlo, OISM Studio..."
                  value={brandName}
                  onChange={(e) => setBrandName(e.target.value)}
                  required
                />
              </label>

              <label style={{ gridColumn: '1 / -1' }}>
                <span>Mô tả / Xuất xứ</span>
                <Input
                  placeholder="Thông tin nhà sản xuất hoặc dòng thương hiệu..."
                  value={brandDesc}
                  onChange={(e) => setBrandDesc(e.target.value)}
                />
              </label>

              {editingBrand && (
                <label>
                  <span>Trạng thái</span>
                  <Select
                    value={brandIsActive ? 'active' : 'inactive'}
                    onChange={(e) => setBrandIsActive(e.target.value === 'active')}
                  >
                    <option value="active">Đang hoạt động</option>
                    <option value="inactive">Ngừng hoạt động</option>
                  </Select>
                </label>
              )}
            </div>

            <div className="modal-actions">
              <Button type="button" variant="secondary" onClick={() => setBrandModalOpen(false)}>
                Hủy
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Đang lưu...' : 'Lưu thương hiệu'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </>
  );
}
