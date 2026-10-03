# OISM — Omnichannel Inventory and Sales Management System
## Hệ thống quản lý bán hàng và tồn kho đa kênh

> **Workspace:** `d:\OISMS`  
> **Stack:** .NET 8 Web API · PostgreSQL · ReactJS PWA · SignalR · Hangfire · Docker · GitHub Actions  
> **Architecture:** Clean Architecture · SaaS Multi-tenant (Shared DB / TenantId)

---

## 3.2.a — Context

In the modern retail landscape, businesses increasingly operate across multiple sales channels, including physical Point-of-Sale (POS) stores and e-commerce platforms like **Shopee, TikTok Shop, and Lazada**. Common issues include:

- **Stock overselling** — selling out-of-stock items due to delayed inventory synchronization
- **Inaccurate COGS tracking** — incorrect Cost of Goods Sold calculations
- **Data discrepancies** — caused by manual stock updates

The solution requires an integrated, high-concurrency software capable of:
- Real-time inventory ledger tracking
- Automated cost calculations
- Strict anti-oversell mechanisms
- SaaS Multi-tenant architecture

---

## 3.2.b — Proposed Solution

The **OISM** system is built on **Clean Architecture** and **multi-tenant SaaS** principles. Core mechanisms:

| Mechanism | Description |
|---|---|
| **Append-Only Inventory Ledger** | Replaces direct stock updates; enforces absolute auditability |
| **Order Ingestion Hub** | State machine–driven canonical order model |
| **Weighted Average Cost (WAC) Calculator** | Automated COGS recalculation on each purchase receipt |
| **Anti-Oversell Module** | Lock-based reservation engine preventing negative stock |
| **PWA POS Interface** | Touch-optimized, barcode-scanning cashier app |

---

## 3.2.c — Functional Requirements

### FR-AUTH · Multi-tenant & Authentication

| ID | Name | Description |
|---|---|---|
| FR-AUTH-01 | Tenant Registration | Create new Store Tenants with isolated data spaces and unique `TenantId` |
| FR-AUTH-02 | User Authentication | Login/Logout via Email/Phone + Password → JWT tokens (`TenantId`, `UserId`, `Role`) |
| FR-AUTH-03 | RBAC | Roles: **Owner** (full control) · **Staff** (warehouse/order manager) · **Cashier** (POS only) |
| FR-AUTH-04 | Branch Management | Owners can create, update, or toggle visibility for branches and warehouses |

---

### FR-PROD · Product & Variant Management

| ID | Name | Description |
|---|---|---|
| FR-PROD-01 | Category & Brand | Hierarchical product categories + brand catalogs |
| FR-PROD-02 | Product & Variant/SKU | Simple and variant products (Color, Size, etc.); unique SKU per Tenant |
| FR-PROD-03 | Barcode Management | Auto-generate or manual input of EAN-13/Code128 barcodes per SKU |
| FR-PROD-04 | Listed Price | Retail and wholesale prices per SKU (distinct from COGS) |

---

### FR-INV · Inventory Ledger & Stock Management

| ID | Name | Description |
|---|---|---|
| FR-INV-01 | Inventory Ledger Recording | Append-only `InventoryTransaction` ledger: `TenantId`, `BranchId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`. **No direct column updates.** |
| FR-INV-02 | Purchase Receipt | Supplier imports → increases `on_hand` → updates weighted average cost |
| FR-INV-03 | Stock Transfer | 2-step workflow: Out-bound transit → In-bound confirmation between branches |
| FR-INV-04 | Stocktake | Audit sessions, physical counts vs. software balances, generates Adjustment Ledger entries |

---

### FR-COST · Weighted Average Cost Calculation

| ID | Name | Description |
|---|---|---|
| FR-COST-01 | Automated Batch COGS | On Purchase Receipt confirmation: New Cost = ((Old Stock × Old Cost) + (Import Qty × Import Price)) / (Old Stock + Import Qty) |
| FR-COST-02 | COGS Snapshot on Dispatch | Snapshots current WAC into `OrderItem.CostPrice` when order transitions to **Confirmed** or POS checkout |

---

### FR-ORD · Centralized Order Hub

| ID | Name | Description |
|---|---|---|
| FR-ORD-01 | Omnichannel Ingestion | Normalizes orders from POS, Admin manual creation, and Webhook Simulator (Shopee/TikTok/Lazada) into a Canonical Order Model |
| FR-ORD-02 | Order State Machine | **Draft → Reserved → Confirmed → Completed** (or Cancelled) |
| FR-ORD-03 | Order Processing | Staff can review, approve, print shipping slips, or cancel orders |

---

### FR-RSE · Anti-Oversell Mechanism

| ID | Name | Description |
|---|---|---|
| FR-RSE-01 | Available Stock Calc | `available = on_hand - reserved` (dynamic, per SKU per branch) |
| FR-RSE-02 | Stock Reservation | Online orders entering hub → increase `reserved`. Reject if `available < purchased_qty` |
| FR-RSE-03 | Deduct / Release | **Confirmed:** deduct `on_hand`, decrease `reserved`, log ledger. **Cancelled:** decrease `reserved` to release back to pool |

---

### FR-POS · Counter Sales (PWA POS)

| ID | Name | Description |
|---|---|---|
| FR-POS-01 | Fast Sales Interface | Touch + hotkey UI; rapid product search by Name/SKU/Barcode |
| FR-POS-02 | Counter Sales Constraint | Validates `available` stock instantly; blocks sales exceeding available to protect online reservations |
| FR-POS-03 | Checkout & Receipt | Cash, QR Code transfer; auto browser print dialog via Browser Print API |
| FR-POS-04 | Fast Checkout Workflow | **Reserve → Confirm → Deduct** in a single atomic DB transaction at POS payment |

---

### FR-REP · Reports & Alerts

| ID | Name | Description |
|---|---|---|
| FR-REP-01 | Revenue & Gross Profit | Net Revenue, Total COGS, Gross Profit = Revenue − COGS. Filters: Timeframe / Branch / Channel / SKU |
| FR-REP-02 | Inventory & Velocity | Inventory value at cost, best-sellers, slow-moving items |
| FR-REP-03 | Stock Alert | Dashboard notifications when `available <= threshold` (min reorder point per SKU) |

---

### FR-SIM · Webhook Simulator & Real-time Processing

| ID | Name | Description |
|---|---|---|
| FR-SIM-01 | E-commerce Webhook Simulator | Send mock payloads (Shopee/TikTok/Lazada) to test throughput and reservation locks |
| FR-SIM-02 | Real-time Push Notification | **SignalR** pushes sound/popup alerts for new orders to Admin/POS without refresh |
| FR-SIM-03 | Background Processing | **Hangfire** for: auto-cancel expired reserved orders · daily report aggregations |

---

## 3.2.d — Non-Functional Requirements

### NFR-PERF · Performance & Concurrency

| ID | Requirement |
|---|---|
| NFR-PERF-01 | POS SKU search p95 < **200ms** · Order creation < **500ms** · Revenue report < **2s** (<=100k records) |
| NFR-PERF-02 | Flash Sale: 50 concurrent orders on 1 remaining SKU — must not oversell. Use **Pessimistic Locking** (`SELECT FOR UPDATE`) or **Optimistic Locking** (version check) |

### NFR-TENANT · Multi-tenancy & Isolation

| ID | Requirement |
|---|---|
| NFR-TENANT-01 | Shared DB / TenantId pattern. All tables contain `TenantId`. EF Core Global Query Filters enforced — zero data leakage |
| NFR-TENANT-02 | Composite indexes on `(TenantId, CreatedAt)` and `(TenantId, SKUId)` for Ledger and Orders tables |

### NFR-SEC · Security & Data Integrity

| ID | Requirement |
|---|---|
| NFR-SEC-01 | Passwords hashed via BCrypt/Argon2. HTTPS/TLS 1.3. JWT Access Token: 60 min + Refresh Token |
| NFR-SEC-02 | Order Create → Reserve → Deduct must run in a single **ACID atomic transaction** with full rollback |
| NFR-SEC-03 | `InventoryLedger` is strictly append-only — **no UPDATE or DELETE**. Corrections = compensating transactions |

### NFR-USA · Availability & Usability

| ID | Requirement |
|---|---|
| NFR-USA-01 | 99.5% uptime during operating hours (7:00 AM – 10:00 PM) |
| NFR-USA-02 | POS checkout in **<= 3 clicks** or Barcode Scan + Enter. Responsive for desktop, tablet, and mobile handheld |

### NFR-MAINT · Maintainability & Testability

| ID | Requirement |
|---|---|
| NFR-MAINT-01 | Clean Architecture layers: **Domain** (COGS, Reserve logic) · **Application** (use cases) · **Infrastructure** (EF Core, PostgreSQL, SignalR) · **Presentation** (Web API) |
| NFR-MAINT-02 | >= **80% unit & integration test coverage** for: Inventory Ledger · Anti-Oversell Reserve · COGS modules |

---

## 3.2.e — Theory & Methodology

- **Agile** development process
- **UML 2.0** for system modeling

### Required Documents

| Document |
|---|
| User Requirement Specification (URS) & Requirements Traceability Matrix (RTM) |
| Architecture Design Document (Clean Architecture, SaaS Multi-tenancy) |
| Detailed Design: UML Class diagrams, ERD, Sequence diagrams (Stock Reservation & Fast Checkout) |
| System Implementation & Deployment Plan |
| Software Testing Document (Unit, Integration, Concurrency test cases) |
| Installation & User Guide |
| Source code repo + CI/CD (Dockerfiles, `docker-compose.yml`, GitHub Actions) |

---

## 3.2.f — Expected Deliverables

| Deliverable | Description |
|---|---|
| **Backend** | .NET 8 Web API — Multi-tenant auth, inventory ledger, anti-oversell, real-time order processing |
| **Admin Dashboard** | React web app — product/stock/order management, gross profit reports |
| **POS Application** | ReactJS PWA — barcode scanning, fast touch checkout, browser print |
| **Documentation** | Full software engineering docs, Swagger/Postman API specs, deployment guidelines |
| **Demo Package** | Docker Compose bundle: Backend + Web Admin + POS + PostgreSQL + seed data scripts |

---

## 3.2.g — Task Packages

### Task 1 — Core Platform & Infrastructure
- Clean Architecture `.NET 8` solution structure
- Multi-tenant foundation, PostgreSQL schema, EF Core Global Query Filters
- JWT + RBAC Authentication module **(FR-AUTH)**

### Task 2 — Product Catalog & Inventory Ledger
- Product, Variant (SKU), Barcode, Price management APIs **(FR-PROD)**
- Append-Only Inventory Ledger + stock receipt/transfer workflows **(FR-INV)**
- Automated Weighted Average Cost engine **(FR-COST)**

### Task 3 — Order Hub & Anti-Oversell Engine
- Canonical Order Model + State Machine **(FR-ORD)**
- Reserve/Deduct engine with DB Locking **(FR-RSE)**
- Webhook Simulator + Hangfire background jobs **(FR-SIM)**

### Task 4 — POS Frontend & Real-time Integration
- ReactJS Admin Dashboard — inventory, catalog, order management
- ReactJS PWA POS — barcode scan, browser print, atomic checkout **(FR-POS)**
- SignalR real-time notifications **(FR-SIM-02)**

### Task 5 — Reporting, AI, Testing & Deployment
- Revenue / COGS / Gross Profit analytics **(FR-REP)**
- AI forecasting for stock reorder recommendations
- Unit / Integration / Flash Sale concurrency tests
- CI/CD via GitHub Actions + Docker containerization
- Final project documentation

---

*Last updated: 2026-10-03*
