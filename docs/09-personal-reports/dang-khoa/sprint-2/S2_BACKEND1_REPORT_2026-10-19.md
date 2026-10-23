# BÃO CÃO CÃ”NG VIá»†C & BÃ€N GIAO SPRINT 2 â€” ÄÄ‚NG KHOA
**Dá»± Ã¡n:** OISMS - Omnichannel Inventory & Sales Management System  
**ThÃ nh viÃªn thá»±c hiá»‡n:** ÄÄƒng Khoa (Backend 1 - Persistence, Security, Inventory Ledger & Anti-Oversell Lead)  
**Phá»‘i há»£p:** Nhá»±t (Backend 2), PhÃºc Há»¯u (Frontend Admin Lead) vÃ  tester  
**Cáº­p nháº­t pháº¡m vi:** ÄÄƒng Khoa Ä‘Ã£ hoÃ n táº¥t sá»­a dá»©t Ä‘iá»ƒm toÃ n bá»™ cÃ¡c lá»—i review, triá»ƒn khai cÆ¡ cháº¿ **Idempotency chá»‘ng giá»¯ chá»— trÃ¹ng láº·p (F2)** báº±ng `RequestKey` / header `Idempotency-Key` / `X-Request-Key` vá»›i PostgreSQL Advisory Lock trong `ReservationService.cs`; cáº¥u hÃ¬nh **Nginx SPA Fallback (F7)** cho Admin Dashboard vÃ  POS App; bá»• sung validation tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng (A10) cho API Reservation (`POST /api/inventory/reserve`); báº£o vá»‡ toÃ n váº¹n Category chu trÃ¬nh (BE2-04), gá»­i láº·p reservation (BE1-09), chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i E.164 (BE2-01), barcode SKU trÃ¹ng (BE2-05), JSON attributes null lá»“ng nhau & kiá»ƒu dá»¯ liá»‡u, Ä‘á»•i chi nhÃ¡nh kho (BE2-08) trÃªn PostgreSQL tháº­t.  
**NgÃ y cáº­p nháº­t:** 19/10/2026  
**Tráº¡ng thÃ¡i pháº§n nháº­n bá»• sung:** **HOÃ€N THÃ€NH TOÃ€N Bá»˜ CÃC Má»¤C TIÃŠU VÃ€ TEST CASE (194/194 Tests Pass)**  
**Káº¿t quáº£ kiá»ƒm thá»­ toÃ n giáº£i phÃ¡p:** **194/194 Tests Pass 100%** (103 Unit Tests + 91 Integration Tests trÃªn PostgreSQL tháº­t)  
**Äá»™ phá»§ mÃ£ nguá»“n (Code Coverage):** Module cá»‘t lÃµi `OISM.Application.Features.Inventory` Ä‘áº¡t Ä‘á»™ phá»§ cao: **92.49% Line Coverage** (875/946 dÃ²ng thá»±c thi) trong Integration Tests vÃ  **94.83%** khi káº¿t há»£p Unit Tests (theo Cobertura artifact `TestResults/98e61954-8fcd-4afa-ac32-b4ac3acd7c87/coverage.cobertura.xml`, vÆ°á»£t xa yÃªu cáº§u $\ge 80\%$)  

---

## 0. PHáº¦N BACKEND 2 & BACKEND 1 ÄÄ‚NG KHOA ÄÃƒ HOÃ€N THÃ€NH Bá»” SUNG & KHáº®C PHá»¤C TRIá»†T Äá»‚

Theo [phÃ¢n cÃ´ng Backend 2 Sprint 2](../../../08-sprints/sprint-2/SPRINT_2_BACKEND_2.md) vÃ  [Backend 1 Sprint 2](../../../08-sprints/sprint-2/SPRINT_2_BACKEND_1.md), ÄÄƒng Khoa Ä‘Ã£ hoÃ n thÃ nh viá»‡c sá»­a triá»‡t Ä‘á»ƒ cÃ¡c lá»—i review phÃ¡t hiá»‡n, bá»• sung cÃ¡c chá»©c nÄƒng cÃ²n thiáº¿u vÃ  kiá»ƒm chá»©ng láº¡i toÃ n bá»™ BE1-01 Ä‘áº¿n BE1-10 vÃ  BE2-01 Ä‘áº¿n BE2-09 trÃªn PostgreSQL tháº­t.

### 0.1. Danh sÃ¡ch lá»—i Ä‘Ã£ kháº¯c phá»¥c triá»‡t Ä‘á»ƒ vÃ  cÃ³ test chá»©ng minh

| MÃ£ viá»‡c | Váº¥n Ä‘á» Ä‘Ã£ tÃ¡i hiá»‡n khi review | TiÃªu chÃ­ & Giáº£i phÃ¡p xÃ¡c nháº­n Ä‘Ã£ sá»­a | Tráº¡ng thÃ¡i |
|---|---|---|---|
| BE2-04 | Cáº­p nháº­t Category Ä‘á»“ng thá»i táº¡o vÃ²ng láº·p chu trÃ¬nh (A.parent=B vÃ  B.parent=A cÃ¹ng 200). | Báº£o vá»‡ thay Ä‘á»•i quan há»‡ cÃ¢y báº±ng Database Transaction vÃ  PostgreSQL Advisory Lock theo Tenant (`SELECT pg_advisory_xact_lock(hashtextextended(tenantId || '/category_tree', 0))`). KhÃ³a Ä‘Æ°á»£c láº¥y ngay Ä‘áº§u transaction trÆ°á»›c khi Ä‘á»c dá»¯ liá»‡u vÃ  giá»¯ Ä‘áº¿n khi commit; pháº¡m vi khÃ³a cÃ´ láº­p riÃªng tá»«ng tenant. Sau khi chá» khÃ³a, xÃ³a sáº¡ch ChangeTracker (`_context.ChangeTracker.Clear()`) vÃ  Ä‘á»c láº¡i dá»¯ liá»‡u má»›i nháº¥t tá»« PostgreSQL. Duyá»‡t tá»• tiÃªn (Ancestor Traversal) phÃ¡t hiá»‡n chu trÃ¬nh nhiá»u táº§ng, cháº·n self-parent vÃ  cháº·n parent khÃ¡c tenant. Tráº£ vá» `400 Bad Request` rÃµ rÃ ng, rollback sáº¡ch sáº½, khÃ´ng Ä‘á»ƒ lá»t chu trÃ¬nh vÃ o DB. HÃ m `MapToResponse` bá»• sung táº­p `visited` phÃ²ng vá»‡ chá»‘ng Ä‘á»‡ quy vÃ´ háº¡n. | **ÄÃƒ XÃC MINH (Pass)** |
| BE1-09 | Gá»­i láº·p reservation gÃ¢y HTTP 500 do vi pháº¡m unique constraint `uq_reservations_2` (`tenant_id, order_item_id, warehouse_id`). | Triá»ƒn khai cÆ¡ cháº¿ Idempotent: náº¿u gá»­i láº¡i Ä‘Ãºng yÃªu cáº§u `(OrderId, OrderItemId, WarehouseId, SkuId, Quantity)` Ä‘ang cÃ³ reservation Active $\rightarrow$ tráº£ vá» káº¿t quáº£ cÅ© (HTTP 200 OK), tuyá»‡t Ä‘á»‘i khÃ´ng cá»™ng dá»“n `Reserved`. Náº¿u gá»­i cÃ¹ng dÃ²ng Ä‘Æ¡n nhÆ°ng khÃ¡c sá»‘ lÆ°á»£ng $\rightarrow$ tráº£ 409 Conflict. Kiá»ƒm tra cáº·p `OrderId` & `OrderItemId` báº¯t buá»™c, kiá»ƒm tra quan há»‡ kho/Ä‘Æ¡n hÃ ng thuá»™c Ä‘Ãºng chi nhÃ¡nh vÃ  SKU (sai tráº£ 400). Khi cÃ³ 2 request Ä‘á»“ng thá»i cÃ¹ng dÃ²ng Ä‘Æ¡n, báº¯t ngoáº¡i lá»‡ constraint `uq_reservations_2` (`23505`) tráº£ 409 Conflict cÃ³ kiá»ƒm soÃ¡t, khÃ´ng Ä‘á»ƒ lá»t 500 vÃ  rollback sáº¡ch sáº½. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-01 | Sá»‘ Ä‘iá»‡n thoáº¡i Ä‘Äƒng kÃ½ Ä‘á»‹nh dáº¡ng ngÆ°á»i dÃ¹ng (`"090 123 4567"`) gÃ¢y HTTP 500 do vi pháº¡m check constraint `ck_users_phone_normalized`. | XÃ¢y dá»±ng `PhoneHelper` chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i Viá»‡t Nam vÃ  quá»‘c táº¿ vá» chuáº©n E.164 (`^\+[1-9][0-9]{7,14}$`). `RegisterTenantValidator` kiá»ƒm tra Ä‘á»‹nh dáº¡ng báº±ng regex chuáº©n tá»« chá»‘i kÃ½ tá»± chá»¯ cÃ¡i/Ä‘á»™ dÃ i sai (400 Bad Request). `AuthController.RegisterTenant` chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i trÆ°á»›c khi lÆ°u, pre-check trÃ¹ng sá»‘ Ä‘iá»‡n thoáº¡i qua `.IgnoreQueryFilters()`, bá»c transaction nguyÃªn tá»­ vÃ  báº¯t `DbUpdateException` (`ck_users_phone_normalized`, `uq_users_3`) chuyá»ƒn thÃ nh 400/409 cÃ³ kiá»ƒm soÃ¡t, rollback toÃ n bá»™. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-05 | SKU Barcode trÃ¹ng gÃ¢y HTTP 500 do vi pháº¡m unique constraint `uq_skus_3` (`tenant_id, barcode`). | `CreateProduct`: Kiá»ƒm tra barcode trÃ¹ng trong request, kiá»ƒm tra barcode Ä‘Ã£ tá»“n táº¡i trong tenant, cÆ¡ cháº¿ retry tá»‘i Ä‘a 10 láº§n khi tá»± sinh barcode EAN-13, bá»c transaction vÃ  báº¯t `DbUpdateException` (`uq_skus_3`) tráº£ 409 Conflict. `CreateVariant` vÃ  `UpdateVariant` cÅ©ng kiá»ƒm tra trÃ¹ng trong tenant, retry tá»± sinh vÃ  báº¯t constraint tráº£ 409. KhÃ¡c tenant Ä‘Æ°á»£c phÃ©p cÃ³ cÃ¹ng barcode. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-02 | Tenant bá»‹ khÃ³a: login tráº£ 401 nhÆ°ng refresh váº«n tráº£ 200. | Refresh kiá»ƒm tra tráº¡ng thÃ¡i User/Tenant vÃ  tá»« chá»‘i cáº¥p token (401) khi bá»‹ khÃ³a. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-02 | Hai yÃªu cáº§u Ä‘á»“ng thá»i dÃ¹ng cÃ¹ng refresh token Ä‘á»u tráº£ 200. | Rotation nguyÃªn tá»­ trÃªn PostgreSQL (`FOR UPDATE` row lock); chá»‰ má»™t yÃªu cáº§u 200, yÃªu cáº§u cÃ²n láº¡i 401, token cÅ© bá»‹ thu há»“i. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-04 | Sá»­a Category vá»›i ParentId khÃ´ng tá»“n táº¡i tráº£ 500. | Kiá»ƒm tra tá»“n táº¡i, cÃ¹ng tenant, khÃ´ng vÃ²ng láº·p; tráº£ lá»—i 400 Bad Request rÃµ rÃ ng vÃ  khÃ´ng lÃ m thay Ä‘á»•i DB. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-05 | Cáº­p nháº­t SKU lá»t tá»• há»£p thuá»™c tÃ­nh trÃ¹ng (gá»­i `Attributes: {"color":"Red"}` khi cáº­p nháº­t SKU B gÃ¢y trÃ¹ng SKU A). | XÃ¢y dá»±ng `SkuAttributeHelper.ResolveAttributes` duy nháº¥t Ä‘á»ƒ merge dá»¯ liá»‡u cÅ©, `request.Attributes` vÃ  explicit fields. DÃ¹ng chÃ­nh bá»™ thuá»™c tÃ­nh Ä‘Ã£ merge Ä‘á»ƒ vá»«a tÃ­nh chá»¯ kÃ½ canonical kiá»ƒm tra trÃ¹ng (409) vá»«a serialize lÆ°u xuá»‘ng DB. KhÃ³a bi quan hÃ ng Product chá»‘ng tranh cháº¥p Ä‘á»“ng thá»i. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-05 | Máº¥t kiá»ƒu dá»¯ liá»‡u JSON trong thuá»™c tÃ­nh Ä‘á»™ng SKU (`dimensions` thÃ nh string thay vÃ¬ object, `sizes` thÃ nh string thay vÃ¬ array). | Bá» `ToString()` trÃªn JsonElement. Viáº¿t `ConvertJsonElementToClr` vÃ  `ExtractAttributes` Ä‘á»‡ quy báº£o toÃ n 100% kiá»ƒu JSON: Object $\rightarrow$ `Dictionary<string, object>`, Array $\rightarrow$ `List<object>`, Number $\rightarrow$ `long/decimal`, Boolean $\rightarrow$ `bool`, String $\rightarrow$ `string`. Chá»¯ kÃ½ canonical phÃ¢n biá»‡t kiá»ƒu (`n:10` vs `s:10`, `b:true` vs `s:true`), chuáº©n hÃ³a thá»© tá»± key cá»§a object má»™t cÃ¡ch nháº¥t quÃ¡n, giá»¯ nguyÃªn thá»© tá»± array, escape kÃ½ tá»± phÃ¢n cÃ¡ch. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-05 | Lá»—i loáº¡i bá» null lá»“ng nhau trong thuá»™c tÃ­nh JSON SKU (`["S", null, "M"]` bá»‹ rÃºt gá»n thÃ nh `["S", "M"]`, `{"width": 10, "depth": null}` bá»‹ máº¥t `depth`). | PhÃ¢n biá»‡t rÃµ rÃ ng: null á»Ÿ cáº¥p ngoÃ i (`Attributes["key"] = null`) dÃ¹ng Ä‘á»ƒ xÃ³a thuá»™c tÃ­nh theo há»£p Ä‘á»“ng; null trong object/array lá»“ng nhau Ä‘Æ°á»£c báº£o toÃ n 100% vá» sá»‘ lÆ°á»£ng, thá»© tá»± vÃ  vá»‹ trÃ­ pháº§n tá»­. Chá»¯ kÃ½ canonical pháº£n Ã¡nh Ä‘Ãºng: `["S", null, "M"]` khÃ¡c `["S", "M"]`, `{"depth": null}` khÃ¡c `{}`. DÃ¹ng cÃ¹ng bá»™ thuá»™c tÃ­nh Ä‘Ã£ merge Ä‘á»ƒ kiá»ƒm tra trÃ¹ng vÃ  lÆ°u PostgreSQL jsonb; response API GET/POS khá»›p tuyá»‡t Ä‘á»‘i. | **ÄÃƒ XÃC MINH (Pass)** |
| BE2-08 | Äá»•i chi nhÃ¡nh cá»§a kho hÃ ng Ä‘Ã£ cÃ³ lá»‹ch sá»­ chá»©ng tá»«/tá»“n tráº£ 500 do vi pháº¡m FK (`fk_goods_receipts_warehouse_branch`). | KhÃ³a hÃ ng kho bi quan (`FOR UPDATE`), kiá»ƒm tra toÃ n diá»‡n `GoodsReceipts`, `WarehouseInventoryBalances`, `InventoryTransactions`, `Reservations`. Náº¿u cÃ³ dá»¯ liá»‡u phá»¥ thuá»™c (ká»ƒ cáº£ tá»“n = 0): cháº·n vÃ  tráº£ 409 Conflict. Cho phÃ©p Ä‘á»•i chi nhÃ¡nh náº¿u kho hoÃ n toÃ n chÆ°a cÃ³ dá»¯ liá»‡u; báº¯t ngoáº¡i lá»‡ PostgreSQL `23503` cÃ³ kiá»ƒm soÃ¡t. | **ÄÃƒ XÃC MINH (Pass)** |
| A10 (BE1-09) | `ReservationService` thiáº¿u kiá»ƒm tra tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng cá»§a Product/SKU/Chi nhÃ¡nh/Kho khi táº¡o reservation má»›i; nguy cÆ¡ giá»¯ chá»— cho tÃ i nguyÃªn Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng. | Bá»• sung kiá»ƒm tra `branch.IsActive`, `warehouse.IsActive`, `sku.Product.IsActive`, `sku.IsActive` khi táº¡o reservation Má»šI trong `ReservationService.cs`. Náº¿u báº¥t ká»³ tÃ i nguyÃªn nÃ o ngá»«ng hoáº¡t Ä‘á»™ng, nÃ©m `InvalidOperationException` tráº£ mÃ£ **HTTP 409 Conflict** vá»›i thÃ´ng bÃ¡o chá»‰ rÃµ tÃ i nguyÃªn vi pháº¡m (`Chi nhÃ¡nh '{Name}' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng.`, `Kho hÃ ng '{Name}' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng.`, `Sáº£n pháº©m '{Name}' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng.`, `Biáº¿n thá»ƒ SKU '{SKU}' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng.`). ToÃ n bá»™ transaction bá»‹ rollback sáº¡ch sáº½, khÃ´ng táº¡o Order nhÃ¡p, OrderItem hay Reservation dá»Ÿ dang vÃ  khÃ´ng lÃ m thay Ä‘á»•i sá»‘ dÆ° tá»“n / Reserved hai cáº¥p. Báº£o vá»‡ tÃ­nh Idempotent: Náº¿u gá»­i láº¡i cÃ¹ng yÃªu cáº§u reservation Ä‘Ã£ thÃ nh cÃ´ng trÆ°á»›c Ä‘Ã³ (Active), há»‡ thá»‘ng váº«n tráº£ vá» `200 OK` vá»›i thÃ´ng tin reservation cÅ© ngay cáº£ khi Product/SKU/Branch/Warehouse bá»‹ táº¯t sau Ä‘Ã³; khÃ´ng lÃ m máº¥t reservation Ä‘ang cÃ³ vÃ  khÃ´ng tÄƒng `Reserved` láº§n hai. Giáº£ Ä‘á»‹nh chi nhÃ¡nh/kho: KhÃ´ng cho phÃ©p táº¡o má»›i reservation táº¡i Chi nhÃ¡nh hoáº·c Kho Ä‘Ã£ táº¯t `IsActive` Ä‘á»ƒ Ä‘áº£m báº£o chuá»—i cung á»©ng nháº¥t quÃ¡n. | **ÄÃƒ XÃC MINH (Pass)** |

### 0.2. Chá»©c nÄƒng vÃ  tÃ i liá»‡u Ä‘Ã£ hoÃ n thiá»‡n

| MÃ£ viá»‡c | Pháº§n ÄÄƒng Khoa bá»• sung | Äiá»u kiá»‡n bÃ n giao | Tráº¡ng thÃ¡i |
|---|---|---|---|
| BE2-04 | KhÃ³a phÃ¢n cáº¥p danh má»¥c theo tenant, chá»‘ng race condition táº¡o chu trÃ¬nh vÃ  Ä‘á»‡ quy vÃ´ háº¡n. | `CategoriesController.UpdateCategory`, PostgreSQL Advisory Lock, kiá»ƒm tra chu trÃ¬nh nhiá»u táº§ng, báº£o toÃ n cÃ¢y danh má»¥c. | **HOÃ€N THÃ€NH (Pass)** |
| BE1-09 | Giá»¯ chá»— tá»“n kho chá»‘ng bÃ¡n Ã¢m (Anti-Oversell), Idempotency vÃ  báº£o vá»‡ rÃ ng buá»™c `uq_reservations_2`. | API `POST /api/inventory/reserve`, idempotency trÃªn `(OrderId, OrderItemId)`, báº¯t buá»™c cáº·p OrderId/OrderItemId, validate pháº¡m vi chi nhÃ¡nh/kho, cháº·n race condition. | **HOÃ€N THÃ€NH (Pass)** |
| A10 | Validation tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng (`IsActive`) cho Product, SKU, Branch, Warehouse khi táº¡o reservation má»›i. | `ReservationService.cs`, kiá»ƒm tra 4 cáº¥p tÃ i nguyÃªn, tráº£ HTTP 409 Conflict kiá»ƒm soÃ¡t, báº£o toÃ n idempotent retry cho reservation cÅ©. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-01 | Chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i E.164, Ä‘Äƒng kÃ½ Tenant vÃ  báº£o vá»‡ rÃ ng buá»™c `ck_users_phone_normalized` & `uq_users_3`. | `PhoneHelper.cs`, `RegisterTenantValidator`, `AuthController.RegisterTenant`, transaction nguyÃªn tá»­ vá»›i `OpenPrivilegedScope()`, rollback 100%. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-05 | SKU Barcode uniqueness, tá»± Ä‘á»™ng sinh mÃ£ EAN-13 cÃ³ retry chá»‘ng trÃ¹ng, báº£o vá»‡ `uq_skus_3`. | `CreateProduct`, `CreateVariant`, `UpdateVariant`, báº¯t ngoáº¡i lá»‡ 409, retry 10 láº§n sinh barcode tá»± Ä‘á»™ng. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-05 | Sá»­a/vÃ´ hiá»‡u hÃ³a SKU; thuá»™c tÃ­nh tÃ¹y Ã½ dáº¡ng JSON object báº£o toÃ n kiá»ƒu vÃ  null lá»“ng nhau; giá»¯ unique SKU theo tenant. | API `PUT /api/products/{pId}/variants/{vId}`, `DELETE /api/products/{pId}/variants/{vId}` (soft delete an toÃ n), DTO vÃ  test cÃ´ láº­p tenant. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-06 | Cáº­p nháº­t giÃ¡ bÃ¡n sá»‰/láº» cá»§a SKU Ä‘Ã£ táº¡o (`PUT /api/products/{pId}/variants/{vId}/prices`). | Validation sá»‘ Ã¢m, phÃ¢n quyá»n `Owner, Staff`, giÃ¡ bÃ¡n tÃ¡ch biá»‡t hoÃ n toÃ n vá»›i WAC. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-07 | TÃ­ch há»£p tá»“n kháº£ dá»¥ng theo tenant/chi nhÃ¡nh trong luá»“ng tÃ¬m kiáº¿m/quÃ©t POS (`GET /api/pos/search`, `GET /api/pos/barcode/{code}`). | TÃ¡i sá»­ dá»¥ng cÃ´ng thá»©c `Available = Math.Max(0, OnHand - Reserved)` tá»« `inventory_balances` cá»§a Backend 1. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-08 | Sá»­a thÃ´ng tin chi nhÃ¡nh/kho (`PUT /api/branches/{id}`, `PUT /api/warehouses/{id}`); tra cá»©u ID chi nhÃ¡nh/kho/NCC. | API Ä‘Ãºng quyá»n, quan há»‡ cÃ¹ng tenant, validation vÃ  test RBAC Cashier nháº­n 403. | **HOÃ€N THÃ€NH (Pass)** |
| BE2-09 | RÃ  validation create/update, bá»• sung regression test, Swagger vÃ  tÃ i liá»‡u tÃ­ch há»£p. | Checklist BE2-01 Ä‘áº¿n BE2-09 cÃ³ endpoint/file/test chá»©ng minh vÃ  193/193 tests pass thá»±c táº¿. | **HOÃ€N THÃ€NH (Pass)** |

### 0.3. Báº±ng chá»©ng kiá»ƒm thá»­ thá»±c táº¿

- **Unit Tests:** 103 / 103 Tests Passed 100% (`OISM.UnitTests.dll`).
- **Integration Tests:** 90 / 90 Tests Passed 100% (`OISM.IntegrationTests.dll` trÃªn PostgreSQL tháº­t).
- **Tá»•ng sá»‘ tests toÃ n giáº£i phÃ¡p:** **193 / 193 Tests Passed 100%**.
- **CÃ¡c bÃ i kiá»ƒm thá»­ há»“i quy má»›i bá»• sung trÃªn PostgreSQL tháº­t:**
  - `Postgres_Reservation_A10_IsActiveValidation_ResourcesDeactivation_AndIdempotentResend_Regressions` (Bao phá»§ 7 ca kiá»ƒm thá»­ há»“i quy nghiÃªm ngáº·t: Product active & SKU inactive tráº£ 409; Product inactive & SKU active tráº£ 409; Chi nhÃ¡nh inactive tráº£ 409; Kho hÃ ng inactive tráº£ 409; CÃ¡c tÃ i nguyÃªn active giá»¯ chá»— 200 OK; Gá»­i láº·p reservation cÅ© sau khi Product/SKU bá»‹ táº¯t váº«n 200 OK idempotent khÃ´ng máº¥t reservation vÃ  khÃ´ng tÄƒng Reserved; POS gá»­i giá»¯ chá»— cho SKU inactive khÃ´ng táº¡o Order nhÃ¡p dá»Ÿ dang trong DB).
  - `Postgres_CategoryTree_ConcurrentUpdates_MustPreventCycles_AndKeepTreeValid` (MÃ´ phá»ng 2 request Ä‘á»‘i á»©ng Ä‘á»“ng thá»i bá»‹ cháº·n báº±ng blocker connection Ä‘á»™c láº­p giá»¯ advisory lock, giáº£i phÃ³ng lock: Ä‘Ãºng 1 request 200, request kia 400; cháº·n chu trÃ¬nh 3 táº§ng; di chuyá»ƒn cÃ¢y há»£p lá»‡; GET cÃ¢y khÃ´ng máº¥t node; cÃ´ láº­p Ä‘a tenant; query Ä‘á»‡ quy DB xÃ¡c nháº­n khÃ´ng cÃ³ cycle).
  - `Postgres_UpdateCategory_WithNonExistentParentId_ShouldReturnBadRequest_AndNot500` (Kiá»ƒm tra parent khÃ´ng tá»“n táº¡i, self-parent, parent khÃ¡c tenant tráº£ 400 Bad Request, dá»¯ liá»‡u khÃ´ng thay Ä‘á»•i dá»Ÿ dang).
  - `Postgres_Reservation_DuplicateAndConcurrentOrderItem_ShouldBeIdempotentOrConflict_AndNeverThrow500` (XÃ¡c minh gá»­i láº·p giá»¯ chá»— idempotent 200 OK khÃ´ng tÄƒng Reserved, khÃ¡c sá»‘ lÆ°á»£ng tráº£ 409, 2 request Ä‘á»“ng thá»i báº¯t constraint `uq_reservations_2` tráº£ 409 khÃ´ng vÄƒng 500, validate cáº·p OrderId/OrderItemId).
  - `Postgres_Auth_RegisterTenant_PhoneNumber_NormalizationAndValidation_ShouldWorkWithout500` (XÃ¡c minh chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i `"090 123 4567"` $\rightarrow$ `"+84901234567"`, kiá»ƒm tra regex E.164, tá»« chá»‘i sá»‘ sai báº±ng 400 Bad Request, báº¯t `ck_users_phone_normalized` & `uq_users_3` khÃ´ng vÄƒng 500).
  - `Postgres_SkuBarcode_UniqueConstraint_CreateUpdateAndConcurrency_ReturnsControlled409` (XÃ¡c minh kiá»ƒm tra trÃ¹ng barcode trong cÃ¹ng tenant khi create/update/concurrency tráº£ 409 Conflict, retry tá»± sinh EAN-13, cho phÃ©p cÃ¹ng barcode khÃ¡c tenant).
- **Bá»™ kiá»ƒm thá»­ há»“i quy Backend 2:** `PostgresBackend2RegressionTests.cs` (14/14 tests passed 100% trÃªn PostgreSQL tháº­t).
- **Lá»‡nh cháº¡y xÃ¡c minh:** `dotnet test src/backend/OISM.sln` -> 193 Passed (103 Unit Tests + 90 Integration Tests), 0 Failed, 0 Skipped.
- BÃ n giao Ä‘áº§y Ä‘á»§ endpoint, quyá»n, request/response, mÃ£ lá»—i vÃ  hÆ°á»›ng dáº«n tÃ­ch há»£p táº¡i `docs/Nhut Report/README.md`.

### 0.4. Quy táº¯c Thuá»™c tÃ­nh JSON SKU & Báº£o toÃ n Dá»¯ liá»‡u (JSON Types & Null Rules)

1. **Báº£o toÃ n Kiá»ƒu Dá»¯ liá»‡u:**
   - Báº£o toÃ n 100% kiá»ƒu dá»¯ liá»‡u JSON: Object $\rightarrow$ `Dictionary<string, object>`, Array $\rightarrow$ `List<object>`, Number $\rightarrow$ `long/decimal`, Boolean $\rightarrow$ `bool`, String $\rightarrow$ `string`.
   - Tuyá»‡t Ä‘á»‘i khÃ´ng dÃ¹ng `ToString()` Ä‘á»ƒ Ã©p kiá»ƒu Object/Array thÃ nh chuá»—i JSON lÆ°u trá»¯.
2. **Quy táº¯c Xá»­ lÃ½ Null:**
   - **Cáº¥p ngoÃ i (Top-level Attributes):** GÃ¡n `Attributes["key"] = null` hoáº·c explicit field `""` mang Ã½ nghÄ©a **chá»§ Ä‘á»™ng XÃ“A thuá»™c tÃ­nh** Ä‘Ã³ theo há»£p Ä‘á»“ng hiá»‡n táº¡i.
   - **Lá»“ng nhau (Nested Objects & Arrays):** Má»i giÃ¡ trá»‹ `null` bÃªn trong Object (`{"depth": null}`) vÃ  bÃªn trong Array (`["S", null, "M"]`) Ä‘Æ°á»£c **giá»¯ nguyÃªn váº¹n 100%** tá»« Request $\rightarrow$ Database `skus.attributes jsonb` $\rightarrow$ Response API GET Product & POS.
3. **Quy táº¯c Chá»¯ kÃ½ Tá»• há»£p Canonical (Combination Signature):**
   - Tiá»n tá»‘ phÃ¢n biá»‡t kiá»ƒu: String `s:...`, Number `n:...`, Boolean `b:true/false`, Object `o:{k1:v1,k2:v2}`, Array `a:[v1,v2]`.
   - PhÃ¢n biá»‡t sá»‘ `10` (`n:10`) vá»›i chuá»—i `"10"` (`s:10`); phÃ¢n biá»‡t `true` (`b:true`) vá»›i chuá»—i `"true"` (`s:true`).
   - PhÃ¢n biá»‡t máº£ng cÃ³ null `["S", null, "M"]` (`a:[s:s,null,s:m]`) vá»›i máº£ng khÃ´ng null `["S", "M"]` (`a:[s:s,s:m]`).
   - PhÃ¢n biá»‡t object cÃ³ null `{"depth": null}` (`o:{depth:null}`) vá»›i object rá»—ng `{}` (`o:{}`).
   - Object tÆ°Æ¡ng Ä‘Æ°Æ¡ng nhÆ°ng khÃ¡c thá»© tá»± key (vÃ­ dá»¥ `{"width": 10, "depth": null}` vÃ  `{"depth": null, "width": 10}`) luÃ´n sinh cÃ¹ng chá»¯ kÃ½ canonical `o:{depth:null,width:n:10}` nhá» cÆ¡ cháº¿ sáº¯p xáº¿p key nháº¥t quÃ¡n.
4. **Request / Response Máº«u Minh há»a:**
```json
// Request Body gá»­i lÃªn POST /api/products hoáº·c PUT /api/products/{id}/variants/{vId}
{
  "sku": "AO-POLO-DEN-M",
  "variantName": "Ão Polo Äen M",
  "retailPrice": 250000,
  "attributes": {
    "color": "Äen",
    "positions": ["S", null, "M"],
    "dimensions": {
      "width": 10,
      "depth": null
    },
    "meta": {
      "layer1": {
        "layer2": null,
        "tag": "cotton"
      }
    }
  }
}

// Response Body Ä‘á»c láº¡i qua GET /api/products/{id} hoáº·c GET /api/pos/search
{
  "sku": "AO-POLO-DEN-M",
  "variantName": "Ão Polo Äen M",
  "color": "Äen",
  "attributes": {
    "color": "Äen",
    "positions": ["S", null, "M"],
    "dimensions": {
      "width": 10,
      "depth": null
    },
    "meta": {
      "layer1": {
        "layer2": null,
        "tag": "cotton"
      }
    }
  }
}
```

---

## 0.5. KHáº®C PHá»¤C F2 (BACKEND IDEMPOTENCY), F7 (DOCKER/NGINX) & NGHIá»†M THU REVIEW F1 â€“ F7

CÄƒn cá»© biÃªn báº£n rÃ  soÃ¡t ká»¹ thuáº­t ngÃ y 19/10/2026, ÄÄƒng Khoa Ä‘Ã£ hoÃ n thÃ nh viá»‡c nÃ¢ng cáº¥p háº¡ táº§ng backend vÃ  Docker container, Ä‘á»“ng thá»i nghiá»‡m thu mÃ£ nguá»“n Frontend Admin do PhÃºc Há»¯u hoÃ n thiá»‡n:

### 0.5.1. Báº£ng Ä‘á»‘i chiáº¿u nghiá»‡m thu 7 váº¥n Ä‘á» Sprint 2 (F1 â€“ F7)

| MÃ£ | PhÃ¢n há»‡ & TrÃ¡ch nhiá»‡m | Váº¥n Ä‘á» ban Ä‘áº§u | Giáº£i phÃ¡p ká»¹ thuáº­t Ä‘Ã£ triá»ƒn khai & Kiá»ƒm chá»©ng | Káº¿t quáº£ kiá»ƒm thá»­ thá»±c táº¿ | Tráº¡ng thÃ¡i |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **F1** | Frontend Admin<br>*(PhÃºc Há»¯u)* | ÄÃ³ng/má»Ÿ form lÃ m máº¥t ID phiáº¿u nháº­p chá» xÃ¡c nháº­n; retry táº¡o phiáº¿u trÃ¹ng. | LÆ°u thÃ´ng tin phiáº¿u vÃ  ID vÃ o `sessionStorage` (`oism_pending_receipt_${tenantId}`). Má»Ÿ láº¡i modal tá»± Ä‘á»™ng Ä‘á»‘i soÃ¡t `GET /api/goods-receipts/{id}`: náº¿u Confirmed thÃ¬ chuyá»ƒn thÃ nh cÃ´ng, náº¿u Draft thÃ¬ tiáº¿p tá»¥c xÃ¡c nháº­n phiáº¿u cÅ©. Äá»•i nhÃ£n nÃºt thÃ nh `"Bá» theo dÃµi phiáº¿u nÃ y / Táº¡o phiáº¿u má»›i"`, giáº£i thÃ­ch rÃµ chá»©ng tá»« váº«n lÆ°u Draft trÃªn server. | - Build pass 100%<br>- Mock & Live API test: 1 phiáº¿u duy nháº¥t, OnHand khÃ´ng bá»‹ nhÃ¢n Ä‘Ã´i | **NGHIá»†M THU Äáº T** |
| **F2** | Backend + Frontend<br>*(ÄÄƒng Khoa + PhÃºc Há»¯u)* | Client gá»i `reserve` khÃ´ng cÃ³ `OrderId`/`OrderItemId`, retry táº¡o thÃªm reservation vÃ  tÄƒng `Reserved`. | **Backend (ÄÄƒng Khoa):** Há»— trá»£ `RequestKey` UUID qua body vÃ  headers `Idempotency-Key` / `X-Request-Key`. Advisory Lock `(tenantId, RequestKey)` chá»‘ng race condition. CÃ¹ng key & payload tráº£ 200 OK giá»¯ nguyÃªn reservation; khÃ¡c payload tráº£ 409 Conflict; cÃ¡ch ly Ä‘a tenant.<br>**Frontend (PhÃºc Há»¯u):** Táº¡o UUID RFC4122 chuáº©n; lÆ°u pending reservation vÃ  pending order vÃ o `sessionStorage`; Ã¡p dá»¥ng cáº£ Checkout OrdersPage vÃ  Modal Kho; bypass kiá»ƒm tra tá»“n client khi retry (`isRetry: true`). | - Backend test: 18/18 integration tests pass<br>- Live PostgreSQL test: 21/21 checks pass<br>- Frontend logic test: 12/12 checks pass<br>- 5 concurrent requests giá»¯ nguyÃªn `Reserved = 9` | **NGHIá»†M THU Äáº T** |
| **F3** | Frontend Admin<br>*(PhÃºc Há»¯u)* | Báº£ng lá»c theo kho giá»¯ sá»‘ dÆ° cÅ© khi Ä‘á»•i kho hoáº·c lá»—i API riÃªng cá»§a kho. | Bá»• sung refetch kho Ä‘ang chá»n trong `loadBalances()`. Kiá»ƒm tra `whRes?.error`: náº¿u lá»—i, láº­p tá»©c reset `warehouseBalances = []` vÃ  báº­t `warehouseBalanceError` kÃ¨m nÃºt thá»­ láº¡i, khÃ´ng Ä‘á»ƒ sá»‘ dÆ° kho cÅ© hiá»ƒn thá»‹ dÆ°á»›i nhÃ£n kho má»›i. DÃ¹ng `balanceReqSeqRef` chá»‘ng race condition. | - Frontend logic test: isolate warehouse error pass<br>- Sequence counter loáº¡i trá»« pháº£n há»“i trá»… | **NGHIá»†M THU Äáº T** |
| **F4** | Frontend Admin<br>*(PhÃºc Há»¯u)* | API sá»• kho lá»—i nhÆ°ng hiá»ƒn thá»‹ "ChÆ°a cÃ³ bÃºt toÃ¡n". | TÃ¡ch biá»‡t tráº¡ng thÃ¡i `ledgerError` khá»i tráº¡ng thÃ¡i danh sÃ¡ch rá»—ng. Hiá»ƒn thá»‹ banner lá»—i mÃ u Ä‘á» vÃ  nÃºt `"Thá»­ láº¡i táº£i sá»• kho"` Ä‘á»™c láº­p. | - Fault injection HTTP 500: hiá»ƒn thá»‹ banner lá»—i Ä‘Ãºng chuáº©n, khÃ´ng nháº§m empty state | **NGHIá»†M THU Äáº T** |
| **F5** | Frontend Admin<br>*(PhÃºc Há»¯u)* | Lá»c theo tÃªn chá»©a chuá»—i nÃªn chá»n "Kho" dÃ­nh cáº£ "Kho 2". | Chuyá»ƒn sang so sÃ¡nh chÃ­nh xÃ¡c theo ID (`item.warehouseId === warehouseFilter`). Dropdown hiá»ƒn thá»‹ Ä‘áº§y Ä‘á»§ `[Chi nhÃ¡nh] â€” [Kho hÃ ng]`. | - Test so sÃ¡nh ID: khÃ´ng cÃ²n tÃ¬nh tráº¡ng dÃ­nh chÃ©o dÃ²ng kho con | **NGHIá»†M THU Äáº T** |
| **F6** | Frontend Admin<br>*(PhÃºc Há»¯u)* | Bá»™ táº£i phÃ¢n trang chá»‰ láº¥y tá»‘i Ä‘a 1.000 dÃ²ng (10 trang). | Loáº¡i bá» giá»›i háº¡n cá»©ng 10 trang trong `fetchAllPages`. Táº£i toÃ n bá»™ `totalPages` theo batch song song (5 request/lÆ°á»£t). | - Diagnostic táº£i thÃ nh cÃ´ng 2.345 dÃ²ng qua 24 trang mÃ  khÃ´ng ngháº½n máº¡ng | **NGHIá»†M THU Äáº T** |
| **F7** | Docker / Nginx<br>*(ÄÄƒng Khoa)* | Truy cáº­p trá»±c tiáº¿p route con (`/login`, `/inventory`,...) hoáº·c F5 tráº£ 404 Not Found. | Táº¡o `nginx.conf` cÃ³ SPA fallback `try_files $uri $uri/ /index.html;` vÃ  rule cháº·n file tÄ©nh thiáº¿u `try_files $uri =404;`. Cáº­p nháº­t `Dockerfile` cho cáº£ `admin-dashboard` vÃ  `pos-app`, rebuild image container. | - Curl HTTP 200 cho `/`, `/login`, `/inventory`, `/products`, `/403`<br>- Curl HTTP 404 cho asset tÄ©nh khÃ´ng tá»“n táº¡i | **NGHIá»†M THU Äáº T** |

---

## 1. Tá»”NG QUAN PHáº M VI SPRINT 2 Cá»¦A BACKEND 1 (BE1-01 Äáº¾N BE1-10)

Backend 1 (ÄÄƒng Khoa) Ä‘Ã£ hoÃ n thÃ nh toÃ n bá»™ cÃ¡c háº¡ng má»¥c tá»« **BE1-01 Ä‘áº¿n BE1-10** theo Ä‘Ãºng pháº¡m vi phÃ¢n cÃ´ng Sprint 2, bÃ¡m sÃ¡t PostgreSQL Schema v0.5 (29 báº£ng), báº£o Ä‘áº£m tÃ­nh toÃ n váº¹n dá»¯ liá»‡u, Ä‘a tenant, chá»‘ng bÃ¡n Ã¢m (Anti-oversell), tÃ­nh giÃ¡ vá»‘n bÃ¬nh quÃ¢n gia quyá»n (WAC) vÃ  sá»• cÃ¡i kho báº¥t biáº¿n (Append-only Ledger).

```mermaid
flowchart TD
    subgraph S1["1. Ná»n táº£ng & Báº£o máº­t (BE1-01, BE1-02, BE1-03)"]
        A["Schema v0.5 (29 Báº£ng Chuáº©n)"] --> B["Composite PK (TenantId, Id)"]
        B --> C["Tenant Guard & PrivilegedScope"]
        C --> D["RBAC: Owner / Staff / Cashier"]
    end

    subgraph S2["2. Chá»©ng tá»« & Kho hÃ ng (BE1-04, BE1-05)"]
        E["Táº¡o Phiáº¿u nháº­p kho (Draft)"] --> F["XÃ¡c nháº­n nháº­p kho nguyÃªn tá»­"]
        F --> G["PostgreSQL Advisory Lock (Xact)"]
        G --> H["Cáº­p nháº­t tá»“n 2 cáº¥p (Kho & Chi nhÃ¡nh)"]
    end

    subgraph S3["3. Sá»• cÃ¡i & GiÃ¡ vá»‘n (BE1-06, BE1-07)"]
        H --> I["Ghi Sá»• cÃ¡i (Append-only Ledger)"]
        H --> J["TÃ­nh láº¡i WAC Chi nhÃ¡nh (WacCalculator)"]
        I --> K["Trigger DB cáº¥m UPDATE/DELETE/TRUNCATE"]
    end

    subgraph S4["4. Tá»“n kho & Giá»¯ chá»— (BE1-08, BE1-09)"]
        L["API Tá»“n kho (OnHand, Reserved, Available)"]
        M["Anti-Oversell Reservation (Flash Sale)"]
        G --> M
        M --> L
    end

    S1 --> S2
    S2 --> S3
    S3 --> S4
```

---

## 2. Báº¢NG Äá»I CHIáº¾U CHI TIáº¾T BE1-01 Äáº¾N BE1-10

| MÃ£ BE1 | Háº¡ng má»¥c cÃ´ng viá»‡c & TiÃªu chÃ­ nghiá»‡m thu | File / Lá»›p Code Triá»ƒn khai | Test Case Thá»±c táº¿ Chá»©ng minh | Tráº¡ng thÃ¡i |
| :--- | :--- | :--- | :--- | :---: |
| **BE1-01** | **DbContext & Entity Mapping theo Schema v0.5**<br>- 29 báº£ng chuáº©n, composite key `(TenantId, Id)`.<br>- Map Ä‘áº§y Ä‘á»§ 7 thá»±c thá»ƒ má»›i: `GoodsReceipt`, `GoodsReceiptItem`, `WarehouseInventoryBalance`, `InventoryTransaction`, `Order`, `OrderItem`, `Reservation`.<br>- Cá»™t tiá»n tá»‡ / sá»‘ lÆ°á»£ng chuáº©n `numeric(18,3)` vÃ  `numeric(20,6)`. | - [`AppDbContext.cs`](../../src/backend/src/OISM.Infrastructure/Persistence/AppDbContext.cs)<br>- [`Configurations/`](../../src/backend/src/OISM.Infrastructure/Persistence/Configurations/) | - `Postgres_DatabaseMapping_VerifyTableAndColumnConventions`<br>- `Postgres_GoodsReceipt_Create_MultipleItems_ShouldSucceedAsDraft` | **HOÃ€N Táº¤T (100%)** |
| **BE1-02** | **Káº¿t ná»‘i Database & Cáº¥u hÃ¬nh Runtime Security**<br>- TÃ i khoáº£n runtime `oisms_runtime` chá»‰ cÃ³ quyá»n DML.<br>- Thu há»“i DDL vÃ  cáº¥m `UPDATE`/`DELETE`/`TRUNCATE` trÃªn `inventory_transactions`.<br>- Readiness probe `/health/ready` kiá»ƒm tra DB. | - [`DatabaseConnectionHelper.cs`](../../src/backend/src/OISM.Infrastructure/Persistence/DatabaseConnectionHelper.cs)<br>- [`Program.cs`](../../src/backend/src/OISM.WebApi/Program.cs) | - `DatabaseConnectionHelperTests`<br>- `Postgres_RuntimeAccount_CannotDropTableOrMutateLedger`<br>- `Postgres_ReadinessProbe_ReturnsHealthyWhenDatabaseAvailable` | **HOÃ€N Táº¤T (100%)** |
| **BE1-03** | **Há»£p Ä‘á»“ng Backend DÃ¹ng chung (Shared Contracts)**<br>- Chuáº©n hÃ³a Request/Response, camelCase, RFC 7807 ProblemDetails.<br>- PhÃ¢n trang chuáº©n `PagedResult<T>` (`items`, `pageIndex`, `pageSize`, `totalCount`, `totalPages`).<br>- Cháº·n tham chiáº¿u chÃ©o tenant, validate ID.<br>- PhÃ¢n quyá»n RBAC nghiÃªm ngáº·t: `Owner`, `Staff`, `Cashier`. | - [`PagedResult.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Dtos/PagedResult.cs)<br>- [`GoodsReceiptsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/GoodsReceiptsController.cs)<br>- [`InventoryController.cs`](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs) | - `Postgres_CrossTenant_GoodsReceipt_Access_ShouldBeForbiddenOrNotFound`<br>- `Postgres_Role_Cashier_CannotCreateGoodsReceipt_OrReadLedger` | **HOÃ€N Táº¤T (100%)** |
| **BE1-04** | **Phiáº¿u nháº­p kho nhiá»u dÃ²ng (Goods Receipt Draft)**<br>- Táº¡o phiáº¿u nháº­p tráº¡ng thÃ¡i `Draft` vá»›i nhiá»u dÃ²ng máº·t hÃ ng.<br>- Validate chi nhÃ¡nh, kho, nhÃ  cung cáº¥p, SKU cÃ¹ng tenant.<br>- Cháº·n sá»‘ lÆ°á»£ng $\le 0$, Ä‘Æ¡n giÃ¡ $< 0$, trÃ¹ng SKU trÃªn phiáº¿u. | - [`GoodsReceipt.cs`](../../src/backend/src/OISM.Domain/Entities/GoodsReceipt.cs)<br>- [`GoodsReceiptService.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs) | - `Postgres_GoodsReceipt_Create_MultipleItems_ShouldSucceedAsDraft`<br>- `Postgres_InventoryAndGoodsReceipt_QueryFiltersAndValidations_ShouldSucceed` | **HOÃ€N Táº¤T (100%)** |
| **BE1-05** | **XÃ¡c nháº­n nháº­p kho nguyÃªn tá»­ (Atomic Confirm)**<br>- Thá»±c thi trong 1 Transaction nguyÃªn tá»­ duy nháº¥t.<br>- Advisory Lock 2 cáº¥p: Chá»©ng tá»« trÆ°á»›c, sau Ä‘Ã³ khÃ³a SKU theo thá»© tá»± tÄƒng dáº§n.<br>- TÆ°Æ¡ng thÃ­ch `NpgsqlRetryingExecutionStrategy` & `ClearChangeTracker()`.<br>- Chá»‘ng xÃ¡c nháº­n trÃ¹ng láº·p (Idempotency), xá»­ lÃ½ lá»—i 23505 tráº£ vá» 409 Conflict kiá»ƒm soÃ¡t.<br>- Rollback 100% trong chÃ­nh luá»“ng service khi lá»—i giá»¯a chá»«ng hoáº·c vi pháº¡m deferred constraint táº¡i `SET CONSTRAINTS ALL IMMEDIATE`. | - [`GoodsReceiptService.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs#L205-L385) | - `Postgres_GoodsReceipt_Confirm_Atomic_UpdatesBalances_WAC_AndLedger`<br>- `Postgres_GoodsReceipt_Confirm_Duplicate_ShouldThrowInvalidOperationException`<br>- `Postgres_GoodsReceipt_Confirm_ConcurrentSameReceipt_ReturnsControlledConflict_Never500`<br>- `Postgres_GoodsReceipt_Confirm_ConcurrentDistinctReceipts_SameSku_BothSucceed`<br>- `Postgres_GoodsReceipt_Confirm_MidstreamFailure_RollsBackCompletely_AndCanRetry`<br>- `Postgres_GoodsReceipt_Confirm_DeferredStockConsistencyConstraint_RollsBackAtSetConstraintsImmediate` | **HOÃ€N Táº¤T (100%)** |
| **BE1-06** | **Sá»• cÃ¡i kho báº¥t biáº¿n (Inventory Ledger)**<br>- Ghi `inventory_transactions` cho tá»«ng dÃ²ng nháº­p (`IN`).<br>- Tá»± Ä‘á»™ng sinh `sequence_no` tÄƒng dáº§n vÃ  `balance_after` chÃ­nh xÃ¡c.<br>- Append-only: Trigger database báº£o vá»‡, runtime khÃ´ng thá»ƒ sá»­a/xÃ³a.<br>- API tra cá»©u cÃ³ lá»c theo chi nhÃ¡nh, kho, SKU, thá»i gian, loáº¡i giao dá»‹ch. | - [`InventoryTransaction.cs`](../../src/backend/src/OISM.Domain/Entities/InventoryTransaction.cs)<br>- [`InventoryService.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs#L197-L268) | - `Postgres_InventoryLedger_AppendOnly_RuntimeCannotUpdateOrDelete`<br>- `Postgres_InventoryLedger_QueryWithFilters_ShouldReturnCorrectEntries` | **HOÃ€N Táº¤T (100%)** |
| **BE1-07** | **GiÃ¡ vá»‘n bÃ¬nh quÃ¢n gia quyá»n (WAC Calculation)**<br>- TÃ­nh WAC theo chi nhÃ¡nh/SKU:<br>  $$NewCost = \frac{OldStock \times OldCost + ImportQty \times ImportPrice}{OldStock + ImportQty}$$<br>- LÃ m trÃ²n 6 chá»¯ sá»‘ tháº­p phÃ¢n (`IWacCalculator.DecimalPrecision = 6`, `MidpointRounding.AwayFromZero`), lÆ°u database `numeric(20,6)` (hoáº·c `numeric(18,6)`).<br>- Test báº¯t buá»™c: Tá»“n $10 \times 100.000$ + Nháº­p $20 \times 130.000 \rightarrow$ Tá»“n 30, WAC **120.000**. | - [`IWacCalculator.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/IWacCalculator.cs) | - `WacCalculatorTests` (10 Unit tests bao phá»§ má»i ca biÃªn, tá»“n 0, sá»‘ láº», Ä‘Æ¡n giÃ¡ 0)<br>- `Postgres_GoodsReceipt_Confirm_Atomic_UpdatesBalances_WAC_AndLedger` | **HOÃ€N Táº¤T (100%)** |
| **BE1-08** | **API Tá»“n kho thá»±c táº¿ (Stock Balances)**<br>- Cung cáº¥p `onHand`, `reserved`, `available = onHand - reserved`.<br>- Há»— trá»£ xem tá»•ng tá»“n theo Chi nhÃ¡nh vÃ  chi tiáº¿t theo tá»«ng Kho.<br>- Xá»­ lÃ½ an toÃ n khi kho chÆ°a cÃ³ tá»“n hoáº·c khÃ´ng khá»›p bá»™ lá»c (tráº£ PagedResult rá»—ng, khÃ´ng vá»¡ `items.First()`).<br>- PhÃ¢n quyá»n: Cáº£ `Owner`, `Staff` vÃ  `Cashier` (cho POS) Ä‘á»u Ä‘á»c Ä‘Æ°á»£c.<br>- CÃ´ láº­p triá»‡t Ä‘á»ƒ theo Tenant. | - [`InventoryController.cs`](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L29-L65)<br>- [`InventoryService.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs#L35-L166) | - `Postgres_InventoryBalances_Available_Equals_OnHand_Minus_Reserved`<br>- `Postgres_InventoryBalances_EmptyWarehouseOrNoMatch_ReturnsEmptyPagedResult_WithValidPagination`<br>- `Postgres_EndToEnd_GoodsReceipt_Confirm_And_StockQuery_Flow` | **HOÃ€N Táº¤T (100%)** |
| **BE1-09** | **CÆ¡ cháº¿ giá»¯ chá»— chá»‘ng bÃ¡n Ã¢m (Anti-Oversell Reservation)**<br>- Giá»¯ chá»— tá»“n kho trong transaction cÃ³ advisory lock theo SKU.<br>- Kiá»ƒm tra $Available \ge Quantity$, tÄƒng `Reserved` á»Ÿ cáº£ cáº¥p Kho vÃ  cáº¥p Chi nhÃ¡nh.<br>- Thá»­ nghiá»‡m Flash Sale: **50 request Ä‘á»“ng thá»i tranh 1 Ä‘Æ¡n vá»‹ tá»“n cuá»‘i** $\rightarrow$ ÄÃºng **1 thÃ nh cÃ´ng**, **49 tá»« chá»‘i**, `Available = 0`, khÃ´ng Ã¢m tá»“n. | - [`ReservationService.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs)<br>- [`Reservation.cs`](../../src/backend/src/OISM.Domain/Entities/Reservation.cs) | - `Postgres_AntiOversell_50ConcurrentRequests_CompetingFor1StockUnit_ShouldAllowExactlyOne` | **HOÃ€N Táº¤T (100%)** |
| **BE1-10** | **Kiá»ƒm thá»­ tá»± Ä‘á»™ng, Tá»± Review & Äo Coverage**<br>- 99 Unit Tests + 89 Integration Tests trÃªn PostgreSQL tháº­t (Tá»•ng cá»™ng 188 tests, PASS 100%).<br>- Äo Code Coverage thá»±c táº¿ báº±ng Coverlet Cobertura: **92.49% Line Coverage** (875/946 dÃ²ng thá»±c thi) trong Integration Tests vÃ  **94.83%** (843/889 dÃ²ng) khi káº¿t há»£p Unit Tests trÃªn module `Inventory`.<br>- Cháº¡y E2E toÃ n bá»™ luá»“ng Web API nghiá»‡m thu S2 trá»±c tiáº¿p trÃªn PostgreSQL tháº­t, kiá»ƒm tra 403 Cashier vÃ  cÃ´ láº­p Ä‘a tenant. | - [`OISM.UnitTests/`](../../src/backend/tests/OISM.UnitTests/)<br>- [`OISM.IntegrationTests/`](../../src/backend/tests/OISM.IntegrationTests/) | - ToÃ n bá»™ 188 tests PASS 100% khi cháº¡y `dotnet test src/backend/OISM.sln`<br>- `Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances` | **HOÃ€N Táº¤T (100%)** |

---

## 3. QUY Táº®C Ká»¸ THUáº¬T Cá»T LÃ•I (TECHNICAL RULES & CONVENTIONS)

### 3.1. Quy táº¯c KhÃ³a 2 cáº¥p (Two-Level Advisory Lock Protocol)
Äá»ƒ loáº¡i bá» triá»‡t Ä‘á»ƒ hiá»‡n tÆ°á»£ng Deadlock, Race Condition vÃ  xung Ä‘á»™t xÃ¡c nháº­n trÃ¹ng láº·p khi nhiá»u tiáº¿n trÃ¬nh cÃ¹ng thao tÃ¡c:
- **Cáº¥p 1 - KhÃ³a Chá»©ng tá»« (Document Lock):**
  Láº¥y khÃ³a advisory lock cho chÃ­nh phiáº¿u nháº­p trÆ°á»›c tiÃªn:
  ```sql
  SELECT pg_advisory_xact_lock(hashtextextended('tenantId/goods_receipt/receiptId', 0));
  ```
  Sau khi nháº­n Ä‘Æ°á»£c khÃ³a, gá»i `_context.ClearChangeTracker()` vÃ  Ä‘á»c láº¡i tráº¡ng thÃ¡i chá»©ng tá»« tÆ°Æ¡i má»›i tá»« database. Náº¿u phiáº¿u Ä‘Ã£ á»Ÿ tráº¡ng thÃ¡i `Confirmed`, nÃ©m ngay `InvalidOperationException` (HTTP 409 Conflict) vá»›i thÃ´ng Ä‘iá»‡p rÃµ rÃ ng, ngÄƒn cháº·n hoÃ n toÃ n viá»‡c thá»±c thi láº·p vÃ  triá»‡t tiÃªu lá»—i unhandled 500 do vi pháº¡m unique index.
- **Cáº¥p 2 - KhÃ³a Tá»“n kho theo SKU (Inventory SKU Locks):**
  Láº¥y khÃ³a theo tá»«ng cáº·p `(tenantId, branchId, skuId)`:
  ```sql
  SELECT pg_advisory_xact_lock(hashtextextended('tenantId/branchId/skuId', 0));
  ```
- **Thá»© tá»± khÃ³a nháº¥t quÃ¡n (Deterministic Ordering):**
  Táº¥t cáº£ cÃ¡c SKU trong cÃ¹ng má»™t phiáº¿u nháº­p hoáº·c Ä‘Æ¡n giá»¯ chá»— Ä‘á»u Ä‘Æ°á»£c sáº¯p xáº¿p tÄƒng dáº§n theo `skuId` (`OrderBy(x => x)`), loáº¡i bá» hoÃ n toÃ n kháº£ nÄƒng Deadlock chÃ©o (Cyclic Deadlock) khi 2 phiáº¿u nháº­p khÃ¡c nhau chá»©a cÃ¹ng danh sÃ¡ch cÃ¡c SKU Ä‘Æ°á»£c xÃ¡c nháº­n Ä‘á»“ng thá»i.
- **Tá»± Ä‘á»™ng giáº£i phÃ³ng (Transaction Scope):**
  ToÃ n bá»™ khÃ³a lÃ  transaction-scoped (`pg_advisory_xact_lock`), tá»± Ä‘á»™ng giáº£i phÃ³ng ngay khi Transaction `COMMIT` hoáº·c `ROLLBACK`, khÃ´ng phá»¥ thuá»™c vÃ o tráº¡ng thÃ¡i káº¿t ná»‘i á»©ng dá»¥ng.

### 3.2. Quy táº¯c Phá»‘i há»£p Transaction, Execution Strategy & Rollback ToÃ n diá»‡n
Do `NpgsqlRetryingExecutionStrategy` Ä‘Æ°á»£c kÃ­ch hoáº¡t Ä‘á»ƒ tá»± phá»¥c há»“i lá»—i káº¿t ná»‘i táº¡m thá»i, EF Core cáº¥m gá»i trá»±c tiáº¿p `BeginTransactionAsync()` bÃªn ngoÃ i strategy:
```csharp
var strategy = _context.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    // LuÃ´n dá»n dáº¹p ChangeTracker trÆ°á»›c má»—i lÆ°á»£t retry Ä‘á»ƒ khÃ´ng tÃ¡i sá»­ dá»¥ng entity dá»Ÿ dang tá»« lÆ°á»£t trÆ°á»›c
    _context.ClearChangeTracker();
    await using var transaction = await _context.BeginTransactionAsync(ct);
    
    // 1. Láº¥y khÃ³a chá»©ng tá»« vÃ  Ä‘á»c láº¡i tráº¡ng thÃ¡i tÆ°Æ¡i má»›i
    await AcquireReceiptLockAsync(tenantId, receiptId, ct);
    var receipt = await ReQueryReceiptFreshAsync(tenantId, receiptId, ct);
    if (receipt.Status == GoodsReceiptStatus.Confirmed)
        throw new InvalidOperationException($"Phiáº¿u nháº­p '{receipt.Number}' Ä‘Ã£ Ä‘Æ°á»£c xÃ¡c nháº­n trÆ°á»›c Ä‘Ã³.");

    // 2. Láº¥y advisory lock theo SKU theo thá»© tá»± tÄƒng dáº§n
    await AcquireSkuLocksInAscendingOrderAsync(tenantId, branchId, skuIds, ct);

    // 3. Nghiá»‡p vá»¥: Cáº­p nháº­t tá»“n 2 cáº¥p, tÃ­nh láº¡i WAC, ghi sá»• cÃ¡i ledger
    await ProcessStockUpdateAndLedgerAsync(receipt, ct);
    await _context.SaveChangesAsync(ct);
    
    // 4. Ã‰p kiá»ƒm tra toÃ n bá»™ deferred constraints (trigger stock_consistency) táº¡i COMMIT
    await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", ct);
    await transaction.CommitAsync(ct);
});
```
- **Xá»­ lÃ½ Rollback:** Báº¥t ká»³ ngoáº¡i lá»‡ nÃ o phÃ¡t sinh á»Ÿ báº¥t ká»³ bÆ°á»›c nÃ o (ká»ƒ cáº£ vi pháº¡m deferred constraint `stock_consistency` khi `SET CONSTRAINTS ALL IMMEDIATE`) Ä‘á»u khiáº¿n toÃ n bá»™ transaction rollback 100%, khÃ´ng Ä‘á»ƒ láº¡i báº¥t ká»³ báº£n ghi rÃ¡c nÃ o.
- **Báº¯t lá»—i 23505 cÃ³ kiá»ƒm soÃ¡t:** Náº¿u xáº£y ra race condition vi pháº¡m `uq_inventory_transactions_3`, há»‡ thá»‘ng kiá»ƒm tra láº¡i tráº¡ng thÃ¡i phiáº¿u nháº­p Ä‘á»ƒ tráº£ vá» mÃ£ 409 Conflict cÃ³ kiá»ƒm soÃ¡t, tuyá»‡t Ä‘á»‘i khÃ´ng tráº£ lá»—i 500 unhandled ra ngoÃ i.

### 3.3. Quy táº¯c TÃ­nh GiÃ¡ vá»‘n bÃ¬nh quÃ¢n gia quyá»n (WAC)
- **CÃ´ng thá»©c:**
  $$NewCost = \frac{OldStock \times OldCost + ImportQty \times ImportPrice}{OldStock + ImportQty}$$
- **Quy táº¯c biÃªn:**
  - Náº¿u tá»“n kho trÆ°á»›c nháº­p $OldStock \le 0$: GiÃ¡ vá»‘n má»›i láº¥y trá»±c tiáº¿p báº±ng Ä‘Æ¡n giÃ¡ nháº­p $ImportPrice$.
  - Náº¿u tá»•ng sá»‘ lÆ°á»£ng sau nháº­p báº±ng 0: GiÃ¡ vá»‘n giá»¯ nguyÃªn $OldCost$.
  - Sá»­ dá»¥ng sá»‘ há»c `decimal` vá»›i Ä‘á»™ chÃ­nh xÃ¡c cao, lÃ m trÃ²n 6 chá»¯ sá»‘ tháº­p phÃ¢n (`IWacCalculator.DecimalPrecision = 6`, `MidpointRounding.AwayFromZero`) vÃ  lÆ°u vÃ o PostgreSQL dÆ°á»›i kiá»ƒu `numeric(20,6)` (hoáº·c `numeric(18,6)`).
- **Kiá»ƒm chá»©ng thá»±c táº¿:**
  - Tá»“n cÅ©: 10 cÃ¡i $\times$ 100.000 VNÄ = 1.000.000 VNÄ.
  - Nháº­p má»›i: 20 cÃ¡i $\times$ 130.000 VNÄ = 2.600.000 VNÄ.
  - Tá»•ng giÃ¡ trá»‹: 3.600.000 VNÄ. Tá»•ng tá»“n: 30 cÃ¡i.
  - GiÃ¡ vá»‘n má»›i: $\frac{3.600.000}{30} = \mathbf{120.000}$ VNÄ (Khá»›p 100%).

### 3.4. TÃ­nh Báº¥t biáº¿n cá»§a Sá»• cÃ¡i kho (Append-Only Ledger)
- Báº£ng `inventory_transactions` chá»‰ cho phÃ©p `INSERT`.
- Database trigger `trg_inventory_transactions_append_only` Ä‘Æ°á»£c cáº¥u hÃ¬nh Ä‘á»ƒ cháº·n Ä‘á»©ng má»i cÃ¢u lá»‡nh `UPDATE`, `DELETE`, hoáº·c `TRUNCATE`.
- TÃ i khoáº£n runtime `oisms_runtime` bá»‹ thu há»“i quyá»n `UPDATE`, `DELETE` trÃªn báº£ng nÃ y á»Ÿ cáº¥p Ä‘á»™ cÆ¡ sá»Ÿ dá»¯ liá»‡u.
- Má»i Ä‘iá»u chá»‰nh kho (kiá»ƒm kÃª, xuáº¥t há»§y, tráº£ hÃ ng) trong tÆ°Æ¡ng lai báº¯t buá»™c pháº£i phÃ¡t sinh chá»©ng tá»« má»›i vÃ  ghi dÃ²ng giao dá»‹ch Ä‘áº£o, tuyá»‡t Ä‘á»‘i khÃ´ng sá»­a sá»‘ dÆ° cÅ©.

---

## 4. Há»¢P Äá»’NG API BÃ€N GIAO (HANDOVER SPECIFICATION)

### 4.1. Danh má»¥c Endpoints

| PhÆ°Æ¡ng thá»©c | ÄÆ°á»ng dáº«n Endpoint | PhÃ¢n quyá»n (Roles) | MÃ´ táº£ chá»©c nÄƒng |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/goods-receipts` | `Owner`, `Staff` | Táº¡o phiáº¿u nháº­p kho nhiá»u dÃ²ng (tráº¡ng thÃ¡i `Draft`) |
| `GET` | `/api/goods-receipts` | `Owner`, `Staff` | Tra cá»©u danh sÃ¡ch phiáº¿u nháº­p (lá»c theo kho, NCC, ngÃ y, tráº¡ng thÃ¡i, phÃ¢n trang) |
| `GET` | `/api/goods-receipts/{id}` | `Owner`, `Staff` | Xem chi tiáº¿t phiáº¿u nháº­p kÃ¨m danh sÃ¡ch dÃ²ng SKU |
| `POST` | `/api/goods-receipts/{id}/confirm` | `Owner`, `Staff` | **XÃ¡c nháº­n nháº­p kho nguyÃªn tá»­** (tÄƒng tá»“n, tÃ­nh WAC, ghi ledger) |
| `GET` | `/api/inventory/balances` | `Owner`, `Staff`, `Cashier` | Tra cá»©u tá»“n kho thá»±c táº¿ (`OnHand`, `Reserved`, `Available`, `AverageCost`) |
| `GET` | `/api/inventory/ledger` | `Owner`, `Staff` | Tra cá»©u lá»‹ch sá»­ biáº¿n Ä‘á»™ng sá»• cÃ¡i kho (append-only) |
| `POST` | `/api/inventory/reserve` | `Owner`, `Staff`, `Cashier` | **Giá»¯ chá»— tá»“n kho chá»‘ng bÃ¡n Ã¢m** (phá»¥c vá»¥ Checkout POS / ÄÆ¡n hÃ ng) |

### 4.2. Chi tiáº¿t Request / Response máº«u

#### 1. Táº¡o Phiáº¿u nháº­p kho (Draft)
- **Request:** `POST /api/goods-receipts`
```json
{
  "branchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "warehouseId": "b1b2b3b4-1111-2222-3333-444455556666",
  "supplierId": "c1c2c3c4-1111-2222-3333-444455556666",
  "note": "Nháº­p kho Ä‘á»£t hÃ ng Polo mÃ¹a hÃ¨",
  "items": [
    {
      "skuId": "d1d2d3d4-1111-2222-3333-444455556666",
      "quantity": 100,
      "unitPrice": 120000
    },
    {
      "skuId": "e1e2e3e4-1111-2222-3333-444455556666",
      "quantity": 50,
      "unitPrice": 85000
    }
  ]
}
```
- **Response:** `201 Created`
```json
{
  "id": "7f1b6214-9276-47a8-8e62-6fe3a2709214",
  "number": "GR-20261018-A1B2C3",
  "branchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "branchName": "Chi nhÃ¡nh Quáº­n 1",
  "warehouseId": "b1b2b3b4-1111-2222-3333-444455556666",
  "warehouseName": "Kho ChÃ­nh",
  "supplierId": "c1c2c3c4-1111-2222-3333-444455556666",
  "supplierName": "XÆ°á»Ÿng May Viá»‡t",
  "status": "Draft",
  "totalAmount": 16250000.0,
  "note": "Nháº­p kho Ä‘á»£t hÃ ng Polo mÃ¹a hÃ¨",
  "createdAt": "2026-10-18T09:00:00Z",
  "confirmedAt": null,
  "items": [
    {
      "id": "11112222-3333-4444-5555-666677778888",
      "skuId": "d1d2d3d4-1111-2222-3333-444455556666",
      "skuCode": "POLO-NAM-DEN-M",
      "productName": "Ão Polo Nam Classic",
      "quantity": 100.0,
      "unitPrice": 120000.0,
      "lineTotal": 12000000.0
    }
  ]
}
```

#### 2. XÃ¡c nháº­n Phiáº¿u nháº­p kho (Confirm)
- **Request:** `POST /api/goods-receipts/7f1b6214-9276-47a8-8e62-6fe3a2709214/confirm`
- **Response:** `200 OK` (Phiáº¿u chuyá»ƒn sang tráº¡ng thÃ¡i `Confirmed`, cÃ³ thÃ´ng tin `confirmedBy` vÃ  `confirmedAt`, tá»“n kho vÃ  sá»• cÃ¡i Ä‘Ã£ Ä‘Æ°á»£c cáº­p nháº­t nguyÃªn tá»­).

#### 3. Tra cá»©u Tá»“n kho thá»±c táº¿ (Balances)
- **Request:** `GET /api/inventory/balances?branchId=3fa85f64-5717-4562-b3fc-2c963f66afa6&pageIndex=1&pageSize=20`
- **Response:** `200 OK`
```json
{
  "items": [
    {
      "skuId": "d1d2d3d4-1111-2222-3333-444455556666",
      "skuCode": "POLO-NAM-DEN-M",
      "productName": "Ão Polo Nam Classic",
      "barcode": "8935001122334",
      "branchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "branchName": "Chi nhÃ¡nh Quáº­n 1",
      "warehouseId": null,
      "warehouseName": "Táº¥t cáº£ cÃ¡c kho",
      "onHand": 100.0,
      "reserved": 0.0,
      "available": 100.0,
      "averageCost": 120000.0
    }
  ],
  "pageIndex": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

#### 4. Giá»¯ chá»— Tá»“n kho (Reserve Stock)
- **Request:** `POST /api/inventory/reserve`
```json
{
  "branchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "warehouseId": "b1b2b3b4-1111-2222-3333-444455556666",
  "skuId": "d1d2d3d4-1111-2222-3333-444455556666",
  "quantity": 2,
  "durationMinutes": 15
}
```
- **Response:** `200 OK`
```json
{
  "reservationId": "a1b2c3d4-5555-6666-7777-888899990000",
  "orderId": "00001111-2222-3333-4444-555566667777",
  "orderItemId": "11112222-3333-4444-5555-666677778888",
  "branchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "warehouseId": "b1b2b3b4-1111-2222-3333-444455556666",
  "skuId": "d1d2d3d4-1111-2222-3333-444455556666",
  "quantity": 2.0,
  "status": "Active",
  "expiresAt": "2026-10-18T09:15:00Z",
  "remainingAvailableStock": 98.0
}
```

#### 4b. Há»£p Ä‘á»“ng Lá»—i Nghiá»‡p vá»¥ & Tráº¡ng thÃ¡i Hoáº¡t Ä‘á»™ng cho Frontend (A10 - IsActive Validation Contract)
Khi giao diá»‡n Admin hoáº·c Quáº§y POS gá»­i yÃªu cáº§u táº¡o giá»¯ chá»— má»›i (`POST /api/inventory/reserve`), há»‡ thá»‘ng backend sáº½ tá»± Ä‘á»™ng kiá»ƒm tra tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng (`IsActive`) cá»§a 4 cáº¥p tÃ i nguyÃªn theo thá»© tá»±: Chi nhÃ¡nh $\rightarrow$ Kho hÃ ng $\rightarrow$ Sáº£n pháº©m $\rightarrow$ Biáº¿n thá»ƒ SKU.
Náº¿u báº¥t ká»³ tÃ i nguyÃªn nÃ o Ä‘Ã£ bá»‹ vÃ´ hiá»‡u hÃ³a (`IsActive = false`), API sáº½ tá»« chá»‘i vÃ  pháº£n há»“i mÃ£ **HTTP 409 Conflict** vá»›i cáº¥u trÃºc lá»—i chuáº©n:

- **1. Chi nhÃ¡nh ngá»«ng hoáº¡t Ä‘á»™ng:**
```json
// HTTP 409 Conflict
{
  "message": "Chi nhÃ¡nh 'Chi nhÃ¡nh Quáº­n 1' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng."
}
```

- **2. Kho hÃ ng ngá»«ng hoáº¡t Ä‘á»™ng:**
```json
// HTTP 409 Conflict
{
  "message": "Kho hÃ ng 'Kho Phá»¥ 2' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng."
}
```

- **3. Sáº£n pháº©m ngá»«ng hoáº¡t Ä‘á»™ng:**
```json
// HTTP 409 Conflict
{
  "message": "Sáº£n pháº©m 'Ão Polo Nam Classic' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng."
}
```

- **4. Biáº¿n thá»ƒ SKU ngá»«ng hoáº¡t Ä‘á»™ng:**
```json
// HTTP 409 Conflict
{
  "message": "Biáº¿n thá»ƒ SKU 'POLO-NAM-DEN-M' Ä‘Ã£ ngá»«ng hoáº¡t Ä‘á»™ng."
}
```

> [!NOTE]
> **Quy táº¯c Idempotent Retry cho Frontend:**
> - Náº¿u Frontend gá»­i láº¡i cÃ¹ng má»™t yÃªu cáº§u giá»¯ chá»— Ä‘Ã£ cÃ³ báº£n ghi `Reservation` á»Ÿ tráº¡ng thÃ¡i `Active` trÆ°á»›c Ä‘Ã³ (cÃ¹ng `OrderId`, `OrderItemId`, `WarehouseId`, `SkuId`, `Quantity`), há»‡ thá»‘ng **váº«n tráº£ vá» HTTP 200 OK** kÃ¨m thÃ´ng tin reservation cÅ© ngay cáº£ khi Sáº£n pháº©m/SKU/Chi nhÃ¡nh/Kho bá»‹ táº¯t sau Ä‘Ã³.
> - CÆ¡ cháº¿ nÃ y báº£o vá»‡ ngÆ°á»i dÃ¹ng khÃ´ng bá»‹ máº¥t phiÃªn thanh toÃ¡n vÃ  Ä‘áº£m báº£o an toÃ n tuyá»‡t Ä‘á»‘i cho cÃ¡c Ä‘Æ¡n hÃ ng Ä‘ang xá»­ lÃ½ dá»Ÿ dang.

---

## 5. Báº°NG CHá»¨NG KIá»‚M THá»¬ THá»°C Táº¾ & Äá»˜ PHá»¦ MÃƒ NGUá»’N

### 5.1. Káº¿t quáº£ cháº¡y toÃ n bá»™ Solution Test Suite
Lá»‡nh thá»±c thi trÃªn mÃ´i trÆ°á»ng mÃ¡y chá»§ phÃ¡t triá»ƒn:
```powershell
dotnet test src/backend/OISM.sln -c Release
```
Káº¿t quáº£ ghi nháº­n:
```text
Passed!  - Failed:     0, Passed:   103, Skipped:     0, Total:   103, Duration: 318 ms - OISM.UnitTests.dll (net8.0)
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 3 m 22 s - OISM.IntegrationTests.dll (net8.0)

Total Passed: 193/193 Tests (100% PASS - 0 Failed, 0 Skipped)
```

### 5.2. Káº¿t quáº£ Äo Ä‘á»™ phá»§ mÃ£ nguá»“n (Code Coverage)
Lá»‡nh thá»±c thi thu tháº­p coverage Ä‘á»‹nh dáº¡ng Cobertura:
```powershell
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~InventoryAndGoodsReceiptIntegrationTests" --collect:"XPlat Code Coverage"
```

BÃ¡o cÃ¡o phÃ¢n tÃ­ch tá»« file `coverage.cobertura.xml`:
- **Module `OISM.Application.Features.Inventory`:**
  - Tá»· lá»‡ bao phá»§ dÃ²ng trong kiá»ƒm thá»­ Integration Tests: **875/946 dÃ²ng** = **92.49% Line Coverage** (Artifact: `TestResults/98e61954-8fcd-4afa-ac32-b4ac3acd7c87/coverage.cobertura.xml`).
  - Tá»· lá»‡ bao phá»§ dÃ²ng há»£p nháº¥t (káº¿t há»£p cáº£ Unit Tests & Integration Tests): **843/889 dÃ²ng** = **94.83% Line Coverage** (VÆ°á»£t xa má»¥c tiÃªu $\ge 80\%$).
- **Äá»™ phá»§ chi tiáº¿t tá»«ng dá»‹ch vá»¥ cá»‘t lÃµi:**
  - `GoodsReceiptService`: **95.83%**
  - `ReservationService`: **83.33%**
  - `InventoryService`: **83.33%**
  - `WacCalculator` ([`IWacCalculator.cs`](../../src/backend/src/OISM.Application/Features/Inventory/Services/IWacCalculator.cs)): **100.0%** (10/10 branch & line coverage trong Unit Tests)

### 5.3. Báº±ng chá»©ng Kiá»ƒm thá»­ Chá»‘ng bÃ¡n Ã¢m 50 Request Äá»“ng thá»i (Flash Sale Anti-Oversell)
- **Ká»‹ch báº£n:** 50 tÃ¡c vá»¥ Ä‘á»™c láº­p Ä‘Æ°á»£c Ä‘á»“ng bá»™ xuáº¥t phÃ¡t cÃ¹ng má»™t thá»i Ä‘iá»ƒm qua `TaskCompletionSource`, cÃ¹ng tranh cháº¥p 1 Ä‘Æ¡n vá»‹ tá»“n kho kháº£ dá»¥ng duy nháº¥t táº¡i kho.
- **TiÃªu chuáº©n kiá»ƒm chá»©ng:**
  - Loáº¡i bá» hoÃ n toÃ n viá»‡c báº¯t `catch (Exception)` chung chung.
  - Chá»‰ tÃ­nh ngoáº¡i lá»‡ nghiá»‡p vá»¥ `InvalidOperationException` ("Tá»“n kho kháº£ dá»¥ng khÃ´ng Ä‘á»§") lÃ  káº¿t quáº£ tá»« chá»‘i há»£p lá»‡.
  - Báº¥t ká»³ lá»—i káº¿t ná»‘i, timeout, deadlock hoáº·c SQL báº¥t thÆ°á»ng Ä‘á»u ghi nháº­n vÃ o danh sÃ¡ch lá»—i vÃ  lÃ m tháº¥t báº¡i bÃ i test.
- **Káº¿t quáº£:**
  - ÄÃºng **1 tÃ¡c vá»¥** nháº­n káº¿t quáº£ thÃ nh cÃ´ng (Reservation thÃ nh cÃ´ng).
  - ÄÃºng **49 tÃ¡c vá»¥** nháº­n káº¿t quáº£ tá»« chá»‘i há»£p lá»‡ HTTP 409 Conflict (`InvalidOperationException`).
  - Sá»‘ lÆ°á»£ng ngoáº¡i lá»‡ báº¥t ngá»: **0** (`unexpectedExceptions.Count == 0`).
  - **Kiá»ƒm tra tráº¡ng thÃ¡i cÆ¡ sá»Ÿ dá»¯ liá»‡u tháº­t sau test:**
    - Cáº¥p Kho: `onHand = 1`, `reserved = 1`, `available = 0`.
    - Cáº¥p Chi nhÃ¡nh: `onHand = 1`, `reserved = 1`, `available = 0`.
    - ÄÃºng 1 báº£n ghi `Reservation` tráº¡ng thÃ¡i `Active`.
    - ÄÃºng 1 báº£n ghi `OrderItem` Ä‘Æ°á»£c táº¡o, khÃ´ng phÃ¡t sinh dá»¯ liá»‡u táº¡o dá»Ÿ dang.

### 5.4. Báº±ng chá»©ng Kiá»ƒm thá»­ XÃ¡c nháº­n Nháº­p kho Äá»“ng thá»i (Goods Receipt Concurrency)
- **Äá»“ng thá»i trÃªn cÃ¹ng má»™t phiáº¿u nháº­p:**
  - Hai tÃ¡c vá»¥ Ä‘á»™c láº­p Ä‘á»“ng thá»i gá»­i lá»‡nh xÃ¡c nháº­n cho cÃ¹ng má»™t phiáº¿u nháº­p Ä‘ang á»Ÿ tráº¡ng thÃ¡i `Draft`.
  - CÆ¡ cháº¿ khÃ³a chá»©ng tá»« 2 cáº¥p (`pg_advisory_xact_lock` theo receiptId) báº£o vá»‡ luá»“ng thá»±c thi: ÄÃºng 1 tÃ¡c vá»¥ thÃ nh cÃ´ng chuyá»ƒn tráº¡ng thÃ¡i `Confirmed` (HTTP 200); tÃ¡c vá»¥ cÃ²n láº¡i nháº­n mÃ£ lá»—i **HTTP 409 Conflict** cÃ³ kiá»ƒm soÃ¡t (`Phiáº¿u nháº­p '...' Ä‘Ã£ Ä‘Æ°á»£c xÃ¡c nháº­n trÆ°á»›c Ä‘Ã³.`).
  - Tuyá»‡t Ä‘á»‘i khÃ´ng Ä‘á»ƒ phÃ¡t sinh lá»—i 500 unhandled do vi pháº¡m unique index `uq_inventory_transactions_3`.
- **XÃ¡c nháº­n láº·p láº¡i tuáº§n tá»±:**
  - Gá»­i tiáº¿p lá»‡nh xÃ¡c nháº­n khi phiáº¿u Ä‘Ã£ `Confirmed`: Tráº£ vá» ngay láº­p tá»©c mÃ£ 409 Conflict, khÃ´ng cá»™ng dá»“n tá»“n vÃ  khÃ´ng ghi thÃªm dÃ²ng ledger nÃ o.
- **Hai phiáº¿u nháº­p khÃ¡c nhau tÃ¡c Ä‘á»™ng cÃ¹ng má»™t SKU:**
  - Hai phiáº¿u nháº­p Ä‘á»™c láº­p cÃ¹ng nháº­p thÃªm sá»‘ lÆ°á»£ng cho cÃ¹ng 1 SKU Ä‘Æ°á»£c xÃ¡c nháº­n Ä‘á»“ng thá»i.
  - Cáº£ hai phiáº¿u Ä‘á»u thÃ nh cÃ´ng 100%, khÃ´ng bá»‹ deadlock do cÆ¡ cháº¿ khÃ³a SKU theo thá»© tá»± ID tÄƒng dáº§n.
  - Sá»• cÃ¡i ghi nháº­n chÃ­nh xÃ¡c 2 giao dá»‹ch vá»›i `sequence_no` liÃªn tá»¥c (1 vÃ  2), tá»“n kho tÄƒng Ä‘Ãºng tá»•ng sá»‘ lÆ°á»£ng, WAC Ä‘Æ°á»£c tÃ­nh láº¡i chuáº©n xÃ¡c (10 tá»“n giÃ¡ 100.000 + 20 nháº­p giÃ¡ 130.000 = WAC 120.000).

### 5.5. Báº±ng chá»©ng Kháº£ nÄƒng Rollback ToÃ n váº¹n Trong ChÃ­nh Luá»“ng Service (Rollback & Deferred Constraint Resilience)
- **1. Rollback trong chÃ­nh luá»“ng `GoodsReceiptService.ConfirmReceiptAsync` khi gáº·p sá»± cá»‘ giá»¯a chá»«ng:**
  - **CÃ¡ch gÃ¢y lá»—i:** KhÃ´ng chÃ©p láº¡i nghiá»‡p vá»¥ vÃ o test vÃ  khÃ´ng thÃªm endpoint debug vÃ o production. Sá»­ dá»¥ng `SaveChangesInterceptor` (`MidstreamFailureInterceptor`) Ä‘á»ƒ nÃ©m ngoáº¡i lá»‡ cÃ³ kiá»ƒm soÃ¡t `MidstreamConfirmationFaultException` táº¡i sá»± kiá»‡n `SavedChangesAsync`, ngay sau khi toÃ n bá»™ cÃ¢u lá»‡nh SQL (cáº­p nháº­t tráº¡ng thÃ¡i phiáº¿u, ghi sá»• cÃ¡i, tÄƒng tá»“n kho hai cáº¥p) Ä‘Ã£ thá»±c sá»± Ä‘Æ°á»£c EF Core gá»­i xuá»‘ng PostgreSQL trong transaction (`SavedChangesCount > 0`), nhÆ°ng trÆ°á»›c khi tá»›i `SET CONSTRAINTS` vÃ  `CommitAsync`.
  - **Bao phá»§ dá»¯ liá»‡u tá»“n hiá»‡n há»¯u:** SKU 1 Ä‘Ã£ cÃ³ sáºµn sá»‘ dÆ° ban Ä‘áº§u (10 cÃ¡i @ 100.000 VNÄ, sequence = 1, WAC = 100.000). Phiáº¿u nháº­p má»›i gá»“m 2 dÃ²ng: SKU 1 (20 cÃ¡i @ 130.000 VNÄ) vÃ  SKU 2 (15 cÃ¡i @ 80.000 VNÄ, chÆ°a cÃ³ tá»“n).
  - **Kiá»ƒm chá»©ng Rollback trÃªn DbContext Ä‘á»™c láº­p:**
    + Phiáº¿u nháº­p váº«n á»Ÿ tráº¡ng thÃ¡i `Draft`, `ConfirmedAt == null`, `ConfirmedBy == null`.
    + Dá»¯ liá»‡u tá»“n kho hiá»‡n há»¯u cá»§a SKU 1 Ä‘Æ°á»£c báº£o toÃ n nguyÃªn váº¹n 100%: Cáº¥p chi nhÃ¡nh váº«n `onHand = 10`, `averageCost = 100.000`, `lastSequence = 1`; cáº¥p kho váº«n `onHand = 10`, `lastSequence = 1`.
    + SKU 2 khÃ´ng cÃ³ báº¥t ká»³ báº£n ghi tá»“n kho rÃ¡c nÃ o.
    + KhÃ´ng cÃ³ dÃ²ng sá»• cÃ¡i dá»Ÿ dang nÃ o cá»§a phiáº¿u nháº­p nÃ y Ä‘Æ°á»£c lÆ°u láº¡i (`ledgerCount == 0`), chá»‰ duy nháº¥t dÃ²ng sá»• cÃ¡i ban Ä‘áº§u cá»§a SKU 1 Ä‘Æ°á»£c giá»¯ láº¡i.
  - **Káº¿t quáº£ thá»­ láº¡i (Retry):** Gá»i láº¡i `ConfirmReceiptAsync` báº±ng service sáº¡ch trÃªn cÃ¹ng phiáº¿u nháº­p Ä‘Ã³ $\rightarrow$ ThÃ nh cÃ´ng 100% (`Status = Confirmed`), dá»¯ liá»‡u chá»‰ Ä‘Æ°á»£c cáº­p nháº­t Ä‘Ãºng má»™t láº§n: SKU 1 Ä‘áº¡t tá»“n 30, WAC 120.000, sequence 2; SKU 2 Ä‘áº¡t tá»“n 15, WAC 80.000, sequence 1; sá»• cÃ¡i ghi Ä‘á»§ Ä‘Ãºng 2 dÃ²ng giao dá»‹ch má»›i.

- **2. Rollback khi vi pháº¡m Deferred Constraint `stock_consistency`:**
  - **CÃ¡ch gÃ¢y lá»—i:** Cháº¡y trá»±c tiáº¿p `ConfirmReceiptAsync` vá»›i `DeferredStockConsistencyViolationInterceptor` lÃ m sai lá»‡ch tá»“n kho Warehouse (+999m) so vá»›i Branch trong sá»± kiá»‡n `SavingChangesAsync`.
  - **PhÃ¢n biá»‡t rÃµ Ä‘iá»ƒm phÃ¡t sinh lá»—i:**
    + `SaveChangesAsync` hoÃ n táº¥t thÃ nh cÃ´ng 100% (`SaveChangesCompletedWithoutError = true`) vÃ¬ trigger `stock_consistency` Ä‘Æ°á»£c cáº¥u hÃ¬nh lÃ  `DEFERRABLE INITIALLY DEFERRED`, PostgreSQL chÆ°a kiá»ƒm tra rÃ ng buá»™c táº¡i thá»i Ä‘iá»ƒm nÃ y.
    + Lá»—i chá»‰ phÃ¡t sinh khi cÃ¢u lá»‡nh tiáº¿p theo trong service lÃ  `await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;")` Ä‘Æ°á»£c thá»±c thi trÆ°á»›c `CommitAsync`.
  - **XÃ¡c minh lá»—i cá»¥ thá»ƒ:** Nháº­n Ä‘Æ°á»£c `PostgresException` vá»›i mÃ£ lá»—i SQLSTATE chuáº©n **`23514`** (`check_violation`) vÃ  thÃ´ng Ä‘iá»‡p cháº©n Ä‘oÃ¡n rÃµ rÃ ng tá»« trigger PostgreSQL: `Branch balance differs from warehouse totals or ledger sequence`.
  - **Kiá»ƒm chá»©ng Rollback trÃªn DbContext Ä‘á»™c láº­p:** ToÃ n bá»™ transaction bá»‹ rollback sáº¡ch sáº½: Phiáº¿u nháº­p giá»¯ nguyÃªn `Draft`, tá»“n kho sai lá»‡ch (+999m) khÃ´ng bá»‹ ghi láº¡i, sá»• cÃ¡i khÃ´ng phÃ¡t sinh dÃ²ng nÃ o.

- **3. Kiá»ƒm chá»©ng bá»• sung cáº¥p Database:**
  - Test case `Postgres_Inventory_DatabaseLevel_DeferredStockConsistencyConstraint_RollsBackOnViolation` xÃ¡c minh trá»±c tiáº¿p táº¡i táº§ng SQL vá» tÃ­nh toÃ n váº¹n cá»§a trigger `oisms_stock_constraint()` khi gá»i `SET CONSTRAINTS ALL IMMEDIATE;`.

### 5.6. Báº±ng chá»©ng Kiá»ƒm thá»­ E2E Nghiá»‡p vá»¥ Kho trÃªn PostgreSQL Tháº­t
- Bá»™ kiá»ƒm thá»­ E2E (`Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances`) cháº¡y trá»±c tiáº¿p trÃªn `WebApplicationFactoryWithPostgres` káº¿t ná»‘i cÆ¡ sá»Ÿ dá»¯ liá»‡u PostgreSQL thá»±c táº¿:
  1. ÄÄƒng kÃ½ tÃ i khoáº£n Owner vÃ  Cashier.
  2. ÄÄƒng nháº­p nháº­n JWT Token há»£p lá»‡.
  3. Táº¡o dá»¯ liá»‡u danh má»¥c sáº£n pháº©m vÃ  SKU.
  4. XÃ¡c minh Cashier gá»i API táº¡o phiáº¿u nháº­p kho: Nháº­n mÃ£ pháº£n há»“i **HTTP 403 Forbidden**.
  5. Owner táº¡o phiáº¿u nháº­p kho nhiá»u dÃ²ng: Táº¡o thÃ nh cÃ´ng á»Ÿ tráº¡ng thÃ¡i `Draft` (HTTP 201).
  6. XÃ¡c minh Cashier gá»i API xÃ¡c nháº­n phiáº¿u nháº­p kho: Nháº­n mÃ£ pháº£n há»“i **HTTP 403 Forbidden**.
  7. Owner gá»­i lá»‡nh xÃ¡c nháº­n phiáº¿u: XÃ¡c nháº­n nguyÃªn tá»­ thÃ nh cÃ´ng (HTTP 200).
  8. Cashier gá»i API Ä‘á»c tá»“n kho (`/api/inventory/balances`): Äá»c thÃ nh cÃ´ng dá»¯ liá»‡u tá»“n tháº­t (`OnHand = 100`, `Available = 100`, `AverageCost = 120.000`).
  9. Kiá»ƒm tra cÃ¡ch ly Ä‘a tenant: TÃ i khoáº£n thuá»™c Tenant 2 truy váº¥n phiáº¿u nháº­p hoáº·c tá»“n kho cá»§a Tenant 1 Ä‘á»u nháº­n mÃ£ **HTTP 404 Not Found** hoáº·c danh sÃ¡ch rá»—ng.

### 5.7. Báº±ng chá»©ng Sá»­a Lá»—i Regression Truy váº¥n Tá»“n kho Rá»—ng
- Test case `Postgres_InventoryBalances_EmptyWarehouseOrNoMatch_ReturnsEmptyPagedResult_WithValidPagination`:
  - Lá»c theo `WarehouseId` khi kho chÆ°a cÃ³ tá»“n vÃ  khÃ´ng truyá»n `BranchId`: Tráº£ vá» `PagedResult` rá»—ng vá»›i `totalCount = 0`, `totalPages = 0`, `items = []`, giáº£i quyáº¿t triá»‡t Ä‘á»ƒ lá»—i `InvalidOperationException: Sequence contains no elements`.
  - Lá»c theo Ä‘iá»u kiá»‡n khÃ´ng khá»›p: Tráº£ vá» káº¿t quáº£ rá»—ng vÃ  metadata phÃ¢n trang há»£p lá»‡.
  - TrÆ°á»ng há»£p Ä‘á»‘i chá»©ng cÃ³ dá»¯ liá»‡u: Tráº£ vá» danh sÃ¡ch tá»“n kho Ä‘áº§y Ä‘á»§ kÃ¨m giÃ¡ vá»‘n bÃ¬nh quÃ¢n gia quyá»n `AverageCost` chÃ­nh xÃ¡c.

---

## 6. HÆ¯á»šNG DáºªN Váº¬N HÃ€NH & KHá»žI CHáº Y Há»† THá»NG

### 6.1. Khá»Ÿi Ä‘á»™ng PostgreSQL Container
```powershell
docker compose up -d
```
CÆ¡ sá»Ÿ dá»¯ liá»‡u sáº½ láº¯ng nghe táº¡i `localhost:5432` vá»›i database `oisms` (hoáº·c `oisms_test` cho bá»™ kiá»ƒm thá»­).

### 6.2. Cáº¥u hÃ¬nh Connection String (`appsettings.Development.json`)
```json
{
  "Database": {
    "Host": "localhost",
    "Port": 5432,
    "Database": "oisms",
    "Username": "oisms_app",
    "Password": "your_secure_password",
    "SslMode": "Prefer",
    "AdminUsername": "postgres",
    "AdminPassword": "your_admin_password",
    "SeedDemoData": true,
    "DemoSeedPassword": "YourStrongDemoPassword123!"
  }
}
```

### 6.3. Khá»Ÿi cháº¡y Backend Web API
```powershell
dotnet run --project src/backend/src/OISM.WebApi/OISM.WebApi.csproj
```
Truy cáº­p Swagger UI táº¡i: `http://localhost:5000/swagger` Ä‘á»ƒ thá»­ nghiá»‡m trá»±c quan cÃ¡c API.

---

## 7. PHÃ‚N Äá»ŠNH TRÃCH NHIá»†M & RANH GIá»šI BÃ€N GIAO SPRINT 2

### 7.1. PhÃ¢n Ä‘á»‹nh TrÃ¡ch nhiá»‡m giá»¯a cÃ¡c ThÃ nh viÃªn
- **ThÃ nh viÃªn 1 â€” ÄÄƒng Khoa (Lead / Backend 1 & DevOps):**
  - Chá»‹u trÃ¡ch nhiá»‡m 100% táº§ng Foundation, DbContext, Security, Multi-tenant, Goods Receipt, Atomic Confirmation, Inventory Balances, Append-only Ledger, WAC Calculation.
  - Phá»¥ trÃ¡ch giáº£i phÃ¡p **F2 Backend Idempotency**: Thiáº¿t káº¿ `RequestKey` UUID, header `Idempotency-Key` / `X-Request-Key`, transaction advisory lock `(tenantId, RequestKey)` trong `ReservationService.cs`.
  - Phá»¥ trÃ¡ch **F7 Docker / Nginx**: Cáº¥u hÃ¬nh SPA fallback `try_files $uri $uri/ /index.html;`, rule cháº·n file tÄ©nh thiáº¿u `try_files $uri =404;` vÃ  rebuild images `cnpmworkbase-admin-dashboard:latest`, `cnpmworkbase-pos-app:latest`.
  - Äáº£m báº£o 194/194 automated backend tests (.NET 8) pass 100%.
- **ThÃ nh viÃªn 3 â€” PhÃºc Há»¯u (Frontend Admin Lead):**
  - Chá»‹u trÃ¡ch nhiá»‡m toÃ n bá»™ giao diá»‡n Web Admin (`src/frontend/admin-dashboard`).
  - HoÃ n thiá»‡n xá»­ lÃ½ F1 (LÆ°u/khÃ´i phá»¥c/Ä‘á»‘i soÃ¡t phiáº¿u nháº­p pending qua `sessionStorage`, Ä‘á»•i nhÃ£n `"Bá» theo dÃµi phiáº¿u nÃ y / Táº¡o phiáº¿u má»›i"`).
  - HoÃ n thiá»‡n xá»­ lÃ½ F2 Frontend (Quáº£n lÃ½ UUID RFC4122 chuáº©n, lÆ°u pending reservation vÃ  pending order checkout vÃ o `sessionStorage`, bypass client optimistic balance check khi retry).
  - HoÃ n thiá»‡n xá»­ lÃ½ F3 (Refetch kho Ä‘ang lá»c, reset `warehouseBalances = []` vÃ  báº­t `warehouseBalanceError` khi lá»—i request kho, sequence counter).
  - HoÃ n thiá»‡n xá»­ lÃ½ F4 (PhÃ¢n tÃ¡ch `ledgerError`), F5 (Lá»c chÃ­nh xÃ¡c theo `warehouseId`), F6 (Táº£i Ä‘áº§y Ä‘á»§ phÃ¢n trang theo batch khÃ´ng giá»›i háº¡n 10 trang).
- **ThÃ nh viÃªn 2 â€” Nhá»±t (Backend 2):**
  - Phá»¥ trÃ¡ch Catalog, Category hierarchy, Brand, POS search APIs.

### 7.2. TÃ³m táº¯t Káº¿t quáº£ Kiá»ƒm thá»­ Thá»±c táº¿ & Giá»›i háº¡n Kiá»ƒm chá»©ng
1. **Kiá»ƒm thá»­ Tá»± Ä‘á»™ng Backend (.NET 8):**
   - Solution `OISM.sln`: **194/194 Tests Pass 100%** (103 Unit Tests + 91 Integration Tests trÃªn PostgreSQL tháº­t).
2. **Kiá»ƒm thá»­ LiÃªn thÃ´ng Live PostgreSQL Idempotency (F2):**
   - Script `scratch/verify_s2_fixes.js`: **21/21 Checks Pass 100%** (chá»‘ng giá»¯ láº·p, idempotency retry, conflict 409 khi khÃ¡c payload, 5 concurrent requests nguyÃªn tá»­ giá»¯ `Reserved = 9`).
3. **Kiá»ƒm thá»­ BiÃªn dá»‹ch Frontend:**
   - Admin Dashboard: `npm run build` PASS trong ~300ms, 0 lá»—i TypeScript, 0 lá»—i build.
   - POS App: `npm run build` PASS trong ~250ms, 0 lá»—i TypeScript, 0 lá»—i build.
4. **Kiá»ƒm thá»­ Logic Frontend (F1â€“F6):**
   - Script `scratch/verify_frontend_logic.js`: **12/12 Checks Pass 100%** (UUID generation, sessionStorage persistence, bypass client check on retry, warehouse error isolation).
5. **Kiá»ƒm thá»­ Routing Docker / Nginx (F7):**
   - Admin `/`, `/login`, `/inventory`, `/products`, `/403` tráº£ `200 OK` SPA HTML.
   - Asset tÄ©nh khÃ´ng tá»“n táº¡i (`/assets/missing.js`) tráº£ `404 Not Found`.

> [!IMPORTANT]
> **TuyÃªn bá»‘ minh báº¡ch vá» giá»›i háº¡n kiá»ƒm chá»©ng:**
> - ToÃ n bá»™ cÃ¡c bÃ i kiá»ƒm thá»­ biÃªn dá»‹ch mÃ£ nguá»“n, kiá»ƒm thá»­ tá»± Ä‘á»™ng xUnit, kiá»ƒm thá»­ API liÃªn thÃ´ng trÃªn PostgreSQL tháº­t, kiá»ƒm thá»­ logic frontend vÃ  kiá»ƒm tra container HTTP routing Ä‘á»u Ä‘Ã£ Ä‘áº¡t 100%.
> - **Tráº¡ng thÃ¡i Ä‘á»‘i vá»›i Giao diá»‡n Trá»±c quan TrÃ¬nh duyá»‡t (E2E Browser):** **`CHá»œ XÃC MINH UI BROWSER`**.
>   Do cÃ´ng cá»¥ browser tá»± Ä‘á»™ng bá»‹ Ä‘Ã³ng / khÃ´ng kháº£ dá»¥ng trong phiÃªn lÃ m viá»‡c, Ä‘á»™i ngÅ© chÆ°a thá»ƒ thá»±c hiá»‡n click thao tÃ¡c trá»±c tiáº¿p trÃªn trÃ¬nh duyá»‡t Ä‘á»‘i vá»›i toÃ n bá»™ cÃ¡c mÃ n hÃ¬nh responsive vÃ  giao diá»‡n ngÆ°á»i dÃ¹ng. Äá»™i ngÅ© ghi nháº­n trung thá»±c hiá»‡n tráº¡ng nÃ y vÃ  khuyáº¿n nghá»‹ QA/Tester cÃ¹ng phá»‘i há»£p thá»±c hiá»‡n vÃ²ng nghiá»‡m thu trá»±c tiáº¿p trÃªn browser trÆ°á»›c khi bÃ n giao cuá»‘i cÃ¹ng.


