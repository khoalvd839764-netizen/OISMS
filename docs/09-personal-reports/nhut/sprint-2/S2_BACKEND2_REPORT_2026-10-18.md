# BÃO CÃO NGHIá»†M THU & TÃ€I LIá»†U BÃ€N GIAO SPRINT 2 (BACKEND 2)
**Dá»± Ã¡n:** OISMS - Omnichannel Inventory & Sales Management System  
**Há»c pháº§n:** Äá»“ Ã¡n ChuyÃªn ngÃ nh / CÃ´ng nghá»‡ Pháº§n má»m  
**ThÃ nh viÃªn thá»±c hiá»‡n:** Nhá»±t PhÃ¹ng Minh (Backend 2 Lead)  
**Phá»‘i há»£p & Nghiá»‡m thu:** ÄÄƒng Khoa (TrÆ°á»Ÿng nhÃ³m / Backend 1 Lead - Kháº¯c phá»¥c lá»—i review, báº£o chá»©ng PostgreSQL & TÃ­ch há»£p)  
**NgÃ y cáº­p nháº­t hoÃ n táº¥t:** 18/10/2026  
**Káº¿t quáº£ kiá»ƒm thá»­ toÃ n diá»‡n:** **188/188 Tests Pass 100%** (99 Unit Tests + 89 Integration Tests trÃªn PostgreSQL tháº­t)  
**MÃ´i trÆ°á»ng cÆ¡ sá»Ÿ dá»¯ liá»‡u kiá»ƒm chá»©ng:** PostgreSQL 16+ tháº­t (database: `oisms_test`, tÃ i khoáº£n runtime: `oisms_test_app`, schema v0.5 - 29 báº£ng)  

---

## 1. Tá»”NG QUAN Káº¾T QUáº¢ KHáº®C PHá»¤C CÃC Lá»–I REVIEW TÃI HIá»†N

ToÃ n bá»™ cÃ¡c váº¥n Ä‘á» ká»¹ thuáº­t phÃ¡t hiá»‡n vÃ  tÃ¡i hiá»‡n trong Ä‘á»£t review Ä‘Ã£ Ä‘Æ°á»£c sá»­a chá»¯a táº­n gá»‘c táº¡i mÃ£ nguá»“n nghiá»‡p vá»¥ vÃ  Ä‘Æ°á»£c chá»©ng minh báº±ng kiá»ƒm thá»­ há»“i quy Ä‘á»™c láº­p trÃªn PostgreSQL tháº­t (`PostgresBackend2RegressionTests.cs`):

```mermaid
flowchart TD
    subgraph BugFixes["Kháº¯c phá»¥c cÃ¡c lá»—i cá»‘t lÃµi Ä‘Ã£ qua review"]
        B1["1. Tenant bá»‹ khÃ³a"] --> S1["Cháº·n cáº£ Login (401) vÃ  Refresh-Token (401)<br>Kiá»ƒm tra IsActive cá»§a User & Tenant"]
        B2["2. Race Condition Refresh Token"] --> S2["Rotation nguyÃªn tá»­ trÃªn PostgreSQL<br>FOR UPDATE Row-Level Lock + ReadCommitted<br>Chá»‰ 1 req 200, req cÃ²n láº¡i 401, token cÅ© thu há»“i"]
        B3["3. Sá»­a Category thiáº¿u ParentId"] --> S3["Kiá»ƒm tra ParentId tá»“n táº¡i & cÃ¹ng Tenant (400)<br>Duyá»‡t tá»• tiÃªn cháº·n Ä‘á»‡ quy (400)<br>KhÃ´ng Ä‘á»ƒ lá»t lá»—i FK gÃ¢y 500"]
        B4["4. Cáº­p nháº­t SKU lá»t tá»• há»£p trÃ¹ng"] --> S4["SkuAttributeHelper.ResolveAttributes<br>Chá»¯ kÃ½ canonical + Serialize tá»« cÃ¹ng dá»¯ liá»‡u merge<br>Cháº·n trÃ¹ng (409) cáº£ create/update + Row Lock"]
        B5["5. Äá»•i chi nhÃ¡nh kho cÃ³ lá»‹ch sá»­ gÃ¢y 500"] --> S5["KhÃ³a hÃ ng FOR UPDATE + Kiá»ƒm tra toÃ n diá»‡n<br>Cháº·n Ä‘á»•i chi nhÃ¡nh (409) náº¿u cÃ³ chá»©ng tá»«/tá»“n/ledger<br>Báº¯t ngoáº¡i lá»‡ FK 23503 cÃ³ kiá»ƒm soÃ¡t"]
        B6["6. Máº¥t kiá»ƒu JSON & Loáº¡i bá» null lá»“ng nhau"] --> S6["Äá»‡ quy ConvertJsonElementToClr + NormalizeRawValue<br>Báº£o toÃ n 100% Object, Array, Number, Bool, String<br>Giá»¯ nguyÃªn null lá»“ng nhau, phÃ¢n biá»‡t chá»¯ kÃ½ canonical"]
        B7["7. Sá»‘ Ä‘iá»‡n thoáº¡i Ä‘Äƒng kÃ½ gÃ¢y lá»—i 500"] --> S7["PhoneHelper E.164 + RegisterTenantValidator (400)<br>Chuáº©n hÃ³a sá»‘ VN vÃ  quá»‘c táº¿, báº£o vá»‡ ck_users_phone_normalized"]
        B8["8. Barcode SKU trÃ¹ng gÃ¢y lá»—i 500"] --> S8["Kiá»ƒm tra trÃ¹ng barcode theo tenant, retry 10 láº§n sinh EAN-13<br>Báº£o vá»‡ uq_skus_3 tráº£ 409 Conflict thay vÃ¬ 500"]
        B9["9. Cáº­p nháº­t Category Ä‘á»“ng thá»i táº¡o vÃ²ng láº·p"] --> S9["PostgreSQL Advisory Lock theo Tenant + ClearChangeTracker<br>Äá»c láº¡i dá»¯ liá»‡u má»›i nháº¥t, duyá»‡t tá»• tiÃªn cháº·n Ä‘á»‡ quy (400)<br>NgÄƒn cháº·n race condition táº¡o chu trÃ¬nh trÃªn DB"]
    end
```

| STT | Váº¥n Ä‘á» review tÃ¡i hiá»‡n | NguyÃªn nhÃ¢n ká»¹ thuáº­t | Giáº£i phÃ¡p triá»ƒn khai thá»±c táº¿ | BÃ i test kiá»ƒm chá»©ng (Passed 100%) | Tráº¡ng thÃ¡i |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **1** | **Tenant bá»‹ khÃ³a nhÆ°ng refresh váº«n tráº£ 200** | Trong `AuthController.RefreshToken`, mÃ£ nguá»“n chá»‰ kiá»ƒm tra `user.IsActive`, bá» quÃªn Ä‘iá»u kiá»‡n `user.Tenant.IsActive`. | Bá»• sung kiá»ƒm tra `if (user.Tenant == null \|\| !user.Tenant.IsActive) return Unauthorized(...)`. | `Postgres_RefreshToken_WhenTenantIsLocked_ShouldReturnUnauthorized` | **ÄÃƒ KHáº®C PHá»¤C** |
| **2** | **2 request Ä‘á»“ng thá»i dÃ¹ng cÃ¹ng refresh token Ä‘á»u tráº£ 200** | Query Ä‘á»c `RevokedAt == null` Ä‘á»“ng thá»i trÆ°á»›c khi commit update, dáº«n Ä‘áº¿n viá»‡c cáº£ hai cÃ¹ng cáº¥p má»›i token (Race condition). | Ãp dá»¥ng Database Transaction vá»›i khÃ³a bi quan PostgreSQL: `SELECT id FROM refresh_tokens WHERE token_hash = @hash FOR UPDATE`. Request thá»© nháº¥t xoay vÃ²ng token vÃ  gÃ¡n `RevokedAt`, Request thá»© hai sau khi unblock Ä‘á»c láº¡i tháº¥y token Ä‘Ã£ bá»‹ revoke nÃªn láº­p tá»©c tráº£ `401 Unauthorized` vÃ  rollback. | `Postgres_ConcurrentRefreshToken_ShouldRotateAtomically_OnlyOneSucceeds` | **ÄÃƒ KHáº®C PHá»¤C** |
| **3** | **Update Category vá»›i ParentId khÃ´ng tá»“n táº¡i tráº£ 500** | `CategoriesController.UpdateCategory` chá»‰ kiá»ƒm tra vÃ²ng láº·p náº¿u tÃ¬m tháº¥y parent. Náº¿u `ParentId` khÃ´ng tá»“n táº¡i, vÃ²ng láº·p thoÃ¡t vÃ  code gÃ¡n trá»±c tiáº¿p vÃ o DB, vi pháº¡m FK constraint PostgreSQL. | Kiá»ƒm tra `parentExists` cÃ¹ng Tenant trÆ°á»›c khi duyá»‡t cÃ¢y. Náº¿u khÃ´ng tá»“n táº¡i hoáº·c khÃ¡c tenant, tráº£ `400 Bad Request` rÃµ rÃ ng, rollback thay Ä‘á»•i. | `Postgres_UpdateCategory_WithNonExistentParentId_ShouldReturnBadRequest_AndNot500` | **ÄÃƒ KHáº®C PHá»¤C** |
| **4** | **Cáº­p nháº­t SKU lá»t tá»• há»£p thuá»™c tÃ­nh trÃ¹ng** | Dá»¯ liá»‡u dÃ¹ng Ä‘á»ƒ tÃ­nh chá»¯ kÃ½ kiá»ƒm tra trÃ¹ng lá»‡ch vá»›i dá»¯ liá»‡u serialize lÆ°u xuá»‘ng database (khi gá»­i `Attributes: {"color":"Red"}` vÃ  explicit `Color` lÃ  null). | XÃ¢y dá»±ng hÃ m `SkuAttributeHelper.ResolveAttributes` duy nháº¥t. Merge dá»¯ liá»‡u cÅ©, `request.Attributes` vÃ  explicit fields (explicit field khÃ´ng rá»—ng cÃ³ Ä‘á»™ Æ°u tiÃªn cao nháº¥t, `""`/`null` xÃ³a thuá»™c tÃ­nh). DÃ¹ng chÃ­nh bá»™ thuá»™c tÃ­nh Ä‘Ã£ merge Ä‘á»ƒ tÃ­nh chá»¯ kÃ½ canonical vÃ  serialize lÆ°u DB. Cháº·n trÃ¹ng tráº£ `409 Conflict`. KhÃ³a bi quan dÃ²ng Product báº±ng `FOR UPDATE`. | `Postgres_UpdateVariant_AttributeCombinationConflict_ShouldReturn409_AndNotCorruptData`<br>`SkuAttributeHelper_ResolveAttributes_AttributesColor_OverridesExisting_WhenExplicitColorIsNull` | **ÄÃƒ KHáº®C PHá»¤C** |
| **5** | **Äá»•i chi nhÃ¡nh cá»§a kho Ä‘Ã£ cÃ³ lá»‹ch sá»­ tráº£ 500** | `WarehousesController.UpdateWarehouse` cho phÃ©p Ä‘á»•i `BranchId`, dáº«n Ä‘áº¿n vi pháº¡m foreign key composite `fk_goods_receipts_warehouse_branch` vÃ  cÃ¡c rÃ ng buá»™c dá»¯ liá»‡u phá»¥ thuá»™c cá»§a kho. | Bá»c trong Transaction cÃ³ row-lock `SELECT id FROM warehouses WHERE id = @id FOR UPDATE`. Kiá»ƒm tra sá»± tá»“n táº¡i cá»§a `GoodsReceipts`, `WarehouseInventoryBalances`, `InventoryTransactions`, `Reservations`. Náº¿u cÃ³ báº¥t ká»³ dá»¯ liá»‡u nÃ o (ká»ƒ cáº£ tá»“n kho = 0): cháº·n vÃ  tráº£ `409 Conflict`. Kho chÆ°a sá»­ dá»¥ng Ä‘á»•i chi nhÃ¡nh sang branch cÃ¹ng tenant; báº¯t ngoáº¡i lá»‡ Postgres `23503` cÃ³ kiá»ƒm soÃ¡t. | `Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds` | **ÄÃƒ KHáº®C PHá»¤C** |
| **6** | **Máº¥t kiá»ƒu JSON & loáº¡i bá» null lá»“ng nhau trong thuá»™c tÃ­nh SKU** | DÃ¹ng `ToString()` Ã©p Object/Array thÃ nh string vÃ  cÃ¡c hÃ m chuyá»ƒn Ä‘á»•i lá»c bá» null (`if (val != null)`) lÃ m máº¥t null lá»“ng nhau (`["S", null, "M"]` thÃ nh `["S", "M"]`, `{"depth": null}` bá»‹ máº¥t `depth`). | Bá» `ToString()`. Chuyá»ƒn Ä‘á»•i Ä‘á»‡ quy CLR types báº£o toÃ n 100% kiá»ƒu JSON vÃ  null lá»“ng nhau. PhÃ¢n biá»‡t rÃµ: null cáº¥p ngoÃ i (`Attributes["key"] = null`) xÃ³a thuá»™c tÃ­nh; null trong object/array lá»“ng nhau Ä‘Æ°á»£c giá»¯ nguyÃªn váº¹n 100%. Chá»¯ kÃ½ canonical pháº£n Ã¡nh Ä‘Ãºng: `["S", null, "M"]` khÃ¡c `["S", "M"]`, `{"depth": null}` khÃ¡c `{}`. DÃ¹ng cÃ¹ng bá»™ thuá»™c tÃ­nh Ä‘á»ƒ kiá»ƒm tra trÃ¹ng vÃ  lÆ°u DB; Ä‘á»c láº¡i tá»« PostgreSQL vÃ  API khá»›p 100%. | `Postgres_SkuAttributes_PreserveNestedNulls_InObjectsAndArrays_AcrossCreateUpdateAndPos`<br>`Postgres_SkuAttributes_PreserveJsonTypes_AcrossCreateUpdateAndDuplicateCheck`<br>`SkuAttributeHelper_PreservesNestedNulls_InObjectsAndArrays` | **ÄÃƒ KHáº®C PHá»¤C** |
| **7** | **Sá»‘ Ä‘iá»‡n thoáº¡i Ä‘Äƒng kÃ½ ngÆ°á»i dÃ¹ng `"090 123 4567"` gÃ¢y HTTP 500 do vi pháº¡m `ck_users_phone_normalized`** | Database Ä‘á»‹nh nghÄ©a check constraint `ck_users_phone_normalized` yÃªu cáº§u chuáº©n E.164 (`^\+[1-9][0-9]{7,14}$`). Request gá»­i khoáº£ng tráº¯ng hoáº·c sá»‘ ná»™i Ä‘á»‹a lÃ m vÄƒng exception PostgreSQL `23514`. | XÃ¢y dá»±ng `PhoneHelper` chuáº©n hÃ³a sá»‘ Viá»‡t Nam (`"090 123 4567"` $\rightarrow$ `"+84901234567"`) vÃ  quá»‘c táº¿. `RegisterTenantValidator` kiá»ƒm tra Ä‘á»‹nh dáº¡ng vÃ  tá»« chá»‘i sá»‘ sai báº±ng `400 Bad Request`. `AuthController.RegisterTenant` chuáº©n hÃ³a trÆ°á»›c khi lÆ°u, pre-check trÃ¹ng sá»‘ Ä‘iá»‡n thoáº¡i qua `.IgnoreQueryFilters()`, bá»c transaction nguyÃªn tá»­ vÃ  báº¯t `DbUpdateException` tráº£ 400/409 rÃµ rÃ ng, rollback sáº¡ch sáº½. | `Postgres_Auth_RegisterTenant_PhoneNumber_NormalizationAndValidation_ShouldWorkWithout500`<br>`RegisterTenantValidator_PhoneNumber_Tests` (23 unit tests) | **ÄÃƒ KHáº®C PHá»¤C** |
| **8** | **SKU Barcode trÃ¹ng gÃ¢y HTTP 500 do vi pháº¡m unique constraint `uq_skus_3`** | Khi client gá»­i barcode trÃ¹ng hoáº·c sinh barcode tá»± Ä‘á»™ng trÃ¹ng vá»›i SKU khÃ¡c trong tenant, EF Core nÃ©m lá»—i DB unhandled `23505`. | `CreateProduct`, `CreateVariant`, `UpdateVariant` kiá»ƒm tra trÃ¹ng barcode trong tenant, retry tá»‘i Ä‘a 10 láº§n khi tá»± sinh EAN-13, bá»c transaction vÃ  báº¯t `DbUpdateException` trÃªn `uq_skus_3` tráº£ `409 Conflict`. Cho phÃ©p cÃ¹ng barcode náº¿u khÃ¡c tenant. | `Postgres_SkuBarcode_UniqueConstraint_CreateUpdateAndConcurrency_ReturnsControlled409`<br>`CreateProduct_DuplicateBarcodeInRequest_ShouldReturnConflict409` | **ÄÃƒ KHáº®C PHá»¤C** |
| **9** | **Cáº­p nháº­t Category Ä‘á»“ng thá»i táº¡o vÃ²ng láº·p chu trÃ¬nh (A.parent=B vÃ  B.parent=A cÃ¹ng 200)** | Khi 2 request Ä‘á»“ng thá»i gá»­i cáº­p nháº­t Category A (ParentId=B) vÃ  Category B (ParentId=A), cáº£ hai cÃ¹ng kiá»ƒm tra cÃ¢y cÅ© trÆ°á»›c khi ghi nÃªn Ä‘á»u tráº£ 200, lÆ°u dá»¯ liá»‡u $A \rightarrow B \rightarrow A$ táº¡o chu trÃ¬nh, lÃ m há»ng cáº¥u trÃºc cÃ¢y danh má»¥c. | Bá»c `UpdateCategory` trong Database Transaction vÃ  láº¥y PostgreSQL Advisory Lock theo Tenant (`pg_advisory_xact_lock(hashtextextended(tenantId \|\| '/category_tree', 0))`). KhÃ³a láº¥y trÆ°á»›c khi Ä‘á»c/kiá»ƒm tra vÃ  giá»¯ Ä‘áº¿n khi commit. Sau khi nháº­n khÃ³a, xÃ³a sáº¡ch ChangeTracker (`_context.ChangeTracker.Clear()`) vÃ  Ä‘á»c láº¡i danh má»¥c tÆ°Æ¡i má»›i tá»« PostgreSQL. Duyá»‡t tá»• tiÃªn (`Ancestor Traversal`) phÃ¡t hiá»‡n chu trÃ¬nh nhiá»u táº§ng, self-parent, parent khÃ¡c tenant. Tráº£ vá» `400 Bad Request` rÃµ rÃ ng, rollback sáº¡ch sáº½, khÃ´ng lÆ°u thay Ä‘á»•i dá»Ÿ dang. Bá»• sung táº­p `visited` trong `MapToResponse` phÃ²ng vá»‡ chá»‘ng Ä‘á»‡ quy vÃ´ háº¡n. | `Postgres_CategoryTree_ConcurrentUpdates_MustPreventCycles_AndKeepTreeValid`<br>`Postgres_UpdateCategory_WithNonExistentParentId_ShouldReturnBadRequest_AndNot500` | **ÄÃƒ KHáº®C PHá»¤C** |

---

## 2. Báº¢NG Äá»I CHIáº¾U CHI TIáº¾T: YÃŠU Cáº¦U (BE2-01 $\rightarrow$ BE2-09) $\rightarrow$ CODE $\rightarrow$ TEST THá»°C Táº¾

Báº£ng dÆ°á»›i Ä‘Ã¢y Ä‘á»‘i chiáº¿u Ä‘áº§y Ä‘á»§ tá»«ng tiÃªu chÃ­ nghiá»‡m thu tá»« tÃ i liá»‡u phÃ¢n cÃ´ng [`docs/Report Sprint 2/SPRINT_2_BACKEND_2.md`](../../../08-sprints/sprint-2/SPRINT_2_BACKEND_2.md) vá»›i cÃ¡c file/lá»›p triá»ƒn khai vÃ  test tá»± Ä‘á»™ng tÆ°Æ¡ng á»©ng:

| MÃ£ viá»‡c | NhÃ³m viá»‡c | Ná»™i dung hoÃ n thiá»‡n chi tiáº¿t | File / Lá»›p Triá»ƒn khai | BÃ i Test Chá»©ng minh (Passed 100%) | Tráº¡ng thÃ¡i |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **BE2-01** | **ÄÄƒng kÃ½ cá»­a hÃ ng** | - Táº¡o Ä‘á»“ng thá»i `Tenant` vÃ  tÃ i khoáº£n `Owner` ban Ä‘áº§u.<br>- **Chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i E.164 (`PhoneHelper`):** Chuáº©n hÃ³a `"090 123 4567"` $\rightarrow$ `"+84901234567"`, tá»« chá»‘i kÃ½ tá»± sai (`400 Bad Request`), báº£o vá»‡ check constraint `ck_users_phone_normalized`.<br>- QuÃ©t trÃ¹ng Email vÃ  SÄT toÃ n cá»¥c qua `.IgnoreQueryFilters()`.<br>- Bá»c trong Transaction ACID vÃ  `OpenPrivilegedScope()`.<br>- Báº¯t ngoáº¡i lá»‡ DB constraint chuyá»ƒn thÃ nh 400/409 rÃµ rÃ ng, rollback 100% náº¿u lá»—i. | - [`AuthController.cs`](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs)<br>- [`RegisterTenantValidator.cs`](../../src/backend/src/OISM.Application/Features/Auth/RegisterTenant/RegisterTenantValidator.cs)<br>- [`PhoneHelper.cs`](../../src/backend/src/OISM.Application/Common/Helpers/PhoneHelper.cs) | - `Postgres_Auth_RegisterTenant_PhoneNumber_NormalizationAndValidation_ShouldWorkWithout500`<br>- `TC_AUTH_01_RegisterTenant_ShouldSucceed_AndAllowLogin`<br>- `RegisterTenant_ShouldReturnConflict_WhenEmailAlreadyExists`<br>- `RegisterTenantValidator_PhoneNumber_Tests` (23 tests) | **HOÃ€N Táº¤T (100%)** |
| **BE2-02** | **XÃ¡c thá»±c ngÆ°á»i dÃ¹ng & Token Rotation** | - Cáº¥p cáº·p JWT Access Token (háº¡n 60p) vÃ  Refresh Token (háº¡n 7 ngÃ y).<br>- BÄƒm máº­t kháº©u BCrypt.<br>- **Token Rotation nguyÃªn tá»­ trÃªn PostgreSQL** (`FOR UPDATE` row-lock): token cÅ© chá»‰ dÃ¹ng Ä‘Æ°á»£c 1 láº§n; cháº·n Ä‘á»©ng race condition Ä‘á»“ng thá»i.<br>- Cháº·n Ä‘Äƒng nháº­p vÃ  refresh khi User hoáº·c Tenant bá»‹ khÃ³a (`401`).<br>- Endpoint thu há»“i `/api/auth/logout` vÃ  há»“ sÆ¡ cÃ¡ nhÃ¢n `/api/auth/me`. | - [`AuthController.cs`](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs)<br>- [`JwtTokenService.cs`](../../src/backend/src/OISM.Infrastructure/Services/JwtTokenService.cs)<br>- [`LoginValidator.cs`](../../src/backend/src/OISM.Application/Features/Auth/Login/LoginValidator.cs) | - `Postgres_RefreshToken_WhenTenantIsLocked_ShouldReturnUnauthorized`<br>- `Postgres_ConcurrentRefreshToken_ShouldRotateAtomically_OnlyOneSucceeds`<br>- `TC_AUTH_01_RegisterTenant_ShouldSucceed_AndAllowLogin`<br>- `Login_ShouldReturnUnauthorized_WhenPasswordIsWrong`<br>- `RefreshToken_ShouldRotateToken_WhenValid` | **HOÃ€N Táº¤T (100%)** |
| **BE2-03** | **PhÃ¢n quyá»n RBAC** | - Cáº¥u hÃ¬nh 3 vai trÃ²: `Owner`, `Staff`, `Cashier`.<br>- Gáº¯n nhÃ£n `[Authorize(Roles = "...")]` trÃªn cÃ¡c endpoint nghiá»‡p vá»¥.<br>- Thu ngÃ¢n (`Cashier`) gá»i nghiá»‡p vá»¥ quáº£n trá»‹ chi nhÃ¡nh, táº¡o kho, sá»­a kho, hoáº·c nháº­p kho bá»‹ cháº·n vá»›i mÃ£ `403 Forbidden`. | - [`BranchController.cs`](../../src/backend/src/OISM.WebApi/Controllers/BranchController.cs)<br>- [`WarehousesController.cs`](../../src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs)<br>- [`ProductsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/ProductsController.cs) | - `Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden`<br>- `TC_AUTH_02_CashierRole_ShouldBeForbidden_WhenAccessingOwnerEndpoints`<br>- `PostgresTenantIsolationTests` | **HOÃ€N Táº¤T (100%)** |
| **BE2-04** | **Danh má»¥c & ThÆ°Æ¡ng hiá»‡u** | - CRUD Category dáº¡ng cÃ¢y phÃ¢n cáº¥p cha - con nhiá»u cáº¥p.<br>- **Báº£o vá»‡ cÃ¢y báº±ng PostgreSQL Advisory Lock theo Tenant** (`pg_advisory_xact_lock(hashtextextended(tenantId \|\| '/category_tree', 0))`) bá»c trong Transaction: cháº·n Ä‘á»©ng race condition khi cáº­p nháº­t Ä‘á»‘i á»©ng Ä‘á»“ng thá»i (A.parent=B vÃ  B.parent=A); xÃ³a sáº¡ch ChangeTracker (`Clear()`) Ä‘á»c dá»¯ liá»‡u má»›i nháº¥t.<br>- **Kiá»ƒm tra ParentId tá»“n táº¡i & cÃ¹ng Tenant:** tá»« chá»‘i `400 Bad Request` náº¿u khÃ´ng há»£p lá»‡.<br>- **Duyá»‡t ngÆ°á»£c tá»• tiÃªn (`Ancestor Traversal`) chá»‘ng Ä‘á»‡ quy láº·p:** cháº·n Ä‘áº·t danh má»¥c con/chÃ¡u lÃ m cha cá»§a chÃ­nh nÃ³ (`400 Bad Request`).<br>- CRUD ThÆ°Æ¡ng hiá»‡u (`Brand`) Ä‘á»™c láº­p theo Tenant, cÃ³ validator. | - [`CategoriesController.cs`](../../src/backend/src/OISM.WebApi/Controllers/CategoriesController.cs)<br>- [`BrandsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/BrandsController.cs)<br>- [`CategoryDtos.cs`](../../src/backend/src/OISM.Application/Features/Catalog/Categories/CategoryDtos.cs)<br>- [`BrandDtos.cs`](../../src/backend/src/OISM.Application/Features/Catalog/Brands/BrandDtos.cs) | - `Postgres_CategoryTree_ConcurrentUpdates_MustPreventCycles_AndKeepTreeValid`<br>- `Postgres_UpdateCategory_WithNonExistentParentId_ShouldReturnBadRequest_AndNot500`<br>- `Category_ShouldRejectCyclicHierarchy`<br>- `UpdateCategoryAndBrandValidator_ShouldFail_WhenNameIsEmpty`<br>- `CatalogAndProductIntegrationTests` | **HOÃ€N Táº¤T (100%)** |
| **BE2-05** | **Sáº£n pháº©m, Biáº¿n thá»ƒ SKU & Thuá»™c tÃ­nh Ä‘á»™ng** | - CRUD Sáº£n pháº©m vÃ  Biáº¿n thá»ƒ SKU (`ProductVariant`) Ä‘áº§y Ä‘á»§.<br>- **Bá»• sung API cáº­p nháº­t biáº¿n thá»ƒ:** `PUT /api/products/{productId}/variants/{variantId}`.<br>- **Bá»• sung vÃ´ hiá»‡u hÃ³a an toÃ n:** `DELETE /api/products/{productId}/variants/{variantId}` (Soft delete `IsActive = false`, báº£o toÃ n dá»¯ liá»‡u lá»‹ch sá»­ kho vÃ  phiáº¿u nháº­p).<br>- **Thuá»™c tÃ­nh tÃ¹y Ã½:** Nháº­n/lÆ°u/tráº£ dÆ°á»›i dáº¡ng JSON object qua cá»™t `attributes jsonb` (thá»a mÃ£n constraint `ck_skus_attributes_object`), báº£o toÃ n 100% kiá»ƒu Object, Array, Number, Bool, String vÃ  null lá»“ng nhau.<br>- **Cháº·n trÃ¹ng tá»• há»£p thuá»™c tÃ­nh:** HÃ m `ResolveAttributes` há»£p nháº¥t dá»¯ liá»‡u cÅ©, JSON attributes vÃ  explicit fields, tÃ­nh chá»¯ kÃ½ canonical vÃ  serialize thá»‘ng nháº¥t 100%, tráº£ vá» `409 Conflict`. KhÃ³a bi quan dÃ²ng Product chá»‘ng tranh cháº¥p Ä‘á»“ng thá»i.<br>- **Unique SKU & Barcode theo Tenant:** CÃ¹ng tenant trÃ¹ng SKU hoáº·c trÃ¹ng Barcode tráº£ `409 Conflict` (báº£o vá»‡ `uq_skus_3`); retry 10 láº§n tá»± sinh EAN-13; khÃ¡c tenant Ä‘Æ°á»£c phÃ©p trÃ¹ng barcode. | - [`ProductsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/ProductsController.cs)<br>- [`ProductDtos.cs`](../../src/backend/src/OISM.Application/Features/Catalog/Products/ProductDtos.cs)<br>- [`SkuAttributeHelper.cs`](../../src/backend/src/OISM.Application/Common/Helpers/SkuAttributeHelper.cs)<br>- [`ProductVariant.cs`](../../src/backend/src/OISM.Domain/Entities/ProductVariant.cs) | - `Postgres_SkuBarcode_UniqueConstraint_CreateUpdateAndConcurrency_ReturnsControlled409`<br>- `Postgres_SkuAttributes_PreserveNestedNulls_InObjectsAndArrays_AcrossCreateUpdateAndPos`<br>- `Postgres_SkuAttributes_PreserveJsonTypes_AcrossCreateUpdateAndDuplicateCheck`<br>- `Postgres_UpdateVariant_AttributeCombinationConflict_ShouldReturn409_AndNotCorruptData`<br>- `Postgres_ProductVariants_DuplicateAttributeCombination_ShouldBeRejectedWith409`<br>- `Postgres_SkuCrud_UpdatePrices_AndSafeSoftDelete_ShouldWorkProperly` | **HOÃ€N Táº¤T (100%)** |
| **BE2-06** | **Quáº£n lÃ½ GiÃ¡ bÃ¡n SKU** | - **Bá»• sung API cáº­p nháº­t giÃ¡ sá»‰/láº»:** `PUT /api/products/{productId}/variants/{variantId}/prices`.<br>- Validation cháº·n giÃ¡ bÃ¡n sá»‘ Ã¢m (`RetailPrice >= 0`, `WholesalePrice >= 0`).<br>- TÃ¡ch biá»‡t hoÃ n toÃ n giÃ¡ bÃ¡n vá»›i GiÃ¡ vá»‘n bÃ¬nh quÃ¢n gia quyá»n (WAC/COGS do Backend 1 tÃ­nh toÃ¡n khi xÃ¡c nháº­n nháº­p kho). Sá»­a giÃ¡ bÃ¡n khÃ´ng lÃ m áº£nh hÆ°á»Ÿng giÃ¡ vá»‘n. | - [`ProductsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/ProductsController.cs)<br>- [`ProductDtos.cs`](../../src/backend/src/OISM.Application/Features/Catalog/Products/ProductDtos.cs) | - `Postgres_SkuCrud_UpdatePrices_AndSafeSoftDelete_ShouldWorkProperly`<br>- `UpdateVariantPricesValidator_ShouldValidateNonNegativePrices`<br>- `CreateVariantValidator_ShouldFail_WhenRetailPriceIsNegative` | **HOÃ€N Táº¤T (100%)** |
| **BE2-07** | **Barcode & TÃ¬m kiáº¿m POS (TÃ­ch há»£p Tá»“n kho)** | - Thuáº­t toÃ¡n sinh vÃ  kiá»ƒm tra mÃ£ váº¡ch quá»‘c táº¿ EAN-13 (check digit Modulo 10).<br>- API tÃ¬m kiáº¿m siÃªu tá»‘c táº¡i quáº§y POS (`GET /api/pos/search?query=...&branchId=...`).<br>- API Barcode Scanner (`GET /api/pos/barcode/{code}?branchId=...`).<br>- **TÃ­ch há»£p Tá»“n kháº£ dá»¥ng theo Chi nhÃ¡nh:** Truy váº¥n tá»« `inventory_balances` cá»§a Backend 1 vÃ  tÃ­nh toÃ¡n theo cÃ´ng thá»©c chuáº©n: `Available = Math.Max(0, OnHand - Reserved)`. | - [`PosCatalogController.cs`](../../src/backend/src/OISM.WebApi/Controllers/PosCatalogController.cs)<br>- [`PosSearchDtos.cs`](../../src/backend/src/OISM.Application/Features/Pos/PosSearchDtos.cs)<br>- [`BarcodeHelper.cs`](../../src/backend/src/OISM.Application/Common/Helpers/BarcodeHelper.cs) | - `Postgres_PosCatalog_SearchAndBarcode_WithBranchStock_ShouldReturnAccurateAvailable`<br>- `BarcodeHelper_ShouldGenerate_ValidEan13`<br>- `BarcodeHelper_ShouldValidate_KnownEan13Barcode`<br>- `BarcodeHelper_ShouldReject_WrongCheckDigit`<br>- `PosCatalog_SearchAndBarcode_ShouldReturnExactProduct` | **HOÃ€N Táº¤T (100%)** |
| **BE2-08** | **Chi nhÃ¡nh, Kho hÃ ng & NhÃ  cung cáº¥p** | - **Chi nhÃ¡nh:** Láº¥y danh sÃ¡ch, tra cá»©u theo ID (`GET /api/branches/{id}`), táº¡o má»›i, báº­t/táº¯t hoáº¡t Ä‘á»™ng vÃ  **sá»­a thÃ´ng tin** (`PUT /api/branches/{id}`).<br>- **Kho hÃ ng:** Láº¥y danh sÃ¡ch (há»— trá»£ lá»c theo `branchId`), tra cá»©u theo ID (`GET /api/warehouses/{id}`), táº¡o má»›i, báº­t/táº¯t hoáº¡t Ä‘á»™ng vÃ  **sá»­a thÃ´ng tin** (`PUT /api/warehouses/{id}`). **Cháº·n Ä‘á»•i chi nhÃ¡nh cá»§a kho cÃ³ lá»‹ch sá»­ chá»©ng tá»«/tá»“n/sá»• cÃ¡i (409 Conflict)**, báº£o vá»‡ quan há»‡ `fk_goods_receipts_warehouse_branch`.<br>- **NhÃ  cung cáº¥p:** Tra cá»©u danh sÃ¡ch, chi tiáº¿t theo ID (`GET /api/suppliers/{id}`), táº¡o má»›i phá»¥c vá»¥ luá»“ng nháº­p kho. | - [`BranchController.cs`](../../src/backend/src/OISM.WebApi/Controllers/BranchController.cs)<br>- [`WarehousesController.cs`](../../src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs)<br>- [`SuppliersController.cs`](../../src/backend/src/OISM.WebApi/Controllers/SuppliersController.cs)<br>- [`BranchDtos.cs`](../../src/backend/src/OISM.Application/Features/Branches/BranchDtos.cs)<br>- [`WarehouseDtos.cs`](../../src/backend/src/OISM.Application/Features/Warehouses/WarehouseDtos.cs) | - `Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`<br>- `Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden`<br>- `Branch_CreateAndToggleStatus_ShouldWork_ForOwner`<br>- `Warehouse_Create_GetByBranch_AndToggleStatus_ShouldWork_ForOwnerAndStaff`<br>- `Supplier_CreateAndSearch_ShouldWorkProperly`<br>- `UpdateBranchAndWarehouseValidator_ShouldValidateCorrectly` | **HOÃ€N Táº¤T (100%)** |
| **BE2-09** | **Validation, Kiá»ƒm thá»­ ToÃ n diá»‡n & Há»£p Ä‘á»“ng API** | - Cáº¥u hÃ¬nh `FluentValidation` Ä‘á»“ng bá»™ cho toÃ n bá»™ Create vÃ  Update DTOs.<br>- Xá»­ lÃ½ chuáº©n xÃ¡c cÃ¡c tÃ¬nh huá»‘ng null, rá»—ng, Ä‘á»™ dÃ i chuá»—i, sá»‘ Ã¢m, trÃ¹ng dá»¯ liá»‡u vÃ  vi pháº¡m rÃ ng buá»™c.<br>- Bá»™ kiá»ƒm thá»­ **188/188 Tests Passed 100%** trÃªn PostgreSQL tháº­t (99 Unit Tests + 89 Integration Tests).<br>- HoÃ n thiá»‡n tÃ i liá»‡u Swagger UI vÃ  há»£p Ä‘á»“ng API bÃ n giao cho Frontend/Tester. | - ToÃ n bá»™ cÃ¡c thÆ° má»¥c DTOs vÃ  Controllers<br>- ThÆ° má»¥c kiá»ƒm thá»­ `tests/OISM.UnitTests/` vÃ  `tests/OISM.IntegrationTests/`<br>- [`Program.cs`](../../src/backend/src/OISM.WebApi/Program.cs) | - ToÃ n bá»™ test suite: 99 Unit Tests + 89 Integration Tests cháº¡y xanh 100%.<br>- BiÃªn dá»‹ch Release: 0 Warning, 0 Error. | **HOÃ€N Táº¤T (100%)** |

---

## 3. DANH Má»¤C API SWAGGER BÃ€N GIAO CHO FRONTEND (ADMIN & POS)

ToÃ n bá»™ cÃ¡c endpoint dÆ°á»›i Ä‘Ã¢y Ä‘Ã£ Ä‘Æ°á»£c cáº¥u hÃ¬nh vÃ  kháº£ dá»¥ng táº¡i Swagger UI:  
ðŸ‘‰ **`http://localhost:5000/swagger`**

| NhÃ³m API | PhÆ°Æ¡ng thá»©c | Endpoint | Quyá»n háº¡n (Role) | MÃ´ táº£ nghiá»‡p vá»¥ bÃ n giao |
| :--- | :---: | :--- | :---: | :--- |
| **XÃ¡c thá»±c** | `POST` | `/api/auth/register-tenant` | Public | ÄÄƒng kÃ½ cá»­a hÃ ng má»›i vÃ  tÃ i khoáº£n Chá»§ shop ban Ä‘áº§u. |
| | `POST` | `/api/auth/login` | Public | ÄÄƒng nháº­p há»‡ thá»‘ng, cáº¥p Access Token & Refresh Token (cháº·n náº¿u tenant bá»‹ khÃ³a). |
| | `POST` | `/api/auth/refresh-token` | Public | LÃ m má»›i Access Token (Token Rotation nguyÃªn tá»­, chá»‘ng race condition, kiá»ƒm tra tenant khÃ³a). |
| | `POST` | `/api/auth/logout` | Public | ÄÄƒng xuáº¥t vÃ  thu há»“i Refresh Token. |
| | `GET` | `/api/auth/me` | Authenticated | Láº¥y há»“ sÆ¡ tÃ i khoáº£n vÃ  danh sÃ¡ch quyá»n cá»§a phiÃªn Ä‘Äƒng nháº­p. |
| **Danh má»¥c** | `GET` | `/api/categories` | Authenticated | Láº¥y danh sÃ¡ch cÃ¢y danh má»¥c phÃ¢n cáº¥p cha - con. |
| | `POST` | `/api/categories` | Owner, Staff | Táº¡o danh má»¥c má»›i (há»— trá»£ chá»n danh má»¥c cha cÃ¹ng tenant). |
| | `PUT` | `/api/categories/{id}` | Owner, Staff | Cáº­p nháº­t danh má»¥c (kiá»ƒm tra cha tá»“n táº¡i, cháº·n Ä‘á»‡ quy láº·p tráº£ 400). |
| | `DELETE` | `/api/categories/{id}` | Owner | VÃ´ hiá»‡u hÃ³a danh má»¥c sáº£n pháº©m. |
| **ThÆ°Æ¡ng hiá»‡u** | `GET` | `/api/brands` | Authenticated | Láº¥y danh sÃ¡ch thÆ°Æ¡ng hiá»‡u sáº£n pháº©m. |
| | `POST` | `/api/brands` | Owner, Staff | Táº¡o thÆ°Æ¡ng hiá»‡u má»›i. |
| | `PUT` | `/api/brands/{id}` | Owner, Staff | Cáº­p nháº­t thÃ´ng tin thÆ°Æ¡ng hiá»‡u. |
| | `DELETE` | `/api/brands/{id}` | Owner | VÃ´ hiá»‡u hÃ³a thÆ°Æ¡ng hiá»‡u. |
| **Sáº£n pháº©m & SKU** | `GET` | `/api/products` | Authenticated | Danh sÃ¡ch sáº£n pháº©m (há»— trá»£ lá»c search, categoryId, brandId) kÃ¨m biáº¿n thá»ƒ vÃ  thuá»™c tÃ­nh JSON. |
| | `GET` | `/api/products/{id}` | Authenticated | Chi tiáº¿t sáº£n pháº©m kÃ¨m danh sÃ¡ch biáº¿n thá»ƒ SKU vÃ  báº£ng giÃ¡. |
| | `POST` | `/api/products` | Owner, Staff | Táº¡o sáº£n pháº©m vÃ  cÃ¡c SKU ban Ä‘áº§u (cháº·n trÃ¹ng SKU vÃ  trÃ¹ng tá»• há»£p thuá»™c tÃ­nh tráº£ 409). |
| | `PUT` | `/api/products/{id}` | Owner, Staff | Cáº­p nháº­t thÃ´ng tin chung cá»§a sáº£n pháº©m (kiá»ƒm tra category/brand cÃ¹ng tenant). |
| | `DELETE` | `/api/products/{id}` | Owner | VÃ´ hiá»‡u hÃ³a sáº£n pháº©m vÃ  toÃ n bá»™ biáº¿n thá»ƒ. |
| | `POST` | `/api/products/{id}/variants` | Owner, Staff | ThÃªm biáº¿n thá»ƒ SKU má»›i (cháº·n trÃ¹ng SKU vÃ  trÃ¹ng tá»• há»£p thuá»™c tÃ­nh tráº£ 409). |
| | `PUT` | `/api/products/{productId}/variants/{variantId}` | Owner, Staff | **[Má»šI]** Cáº­p nháº­t biáº¿n thá»ƒ SKU (thuá»™c tÃ­nh JSON, barcode, giÃ¡, tráº¡ng thÃ¡i; cháº·n trÃ¹ng tá»• há»£p). |
| | `PUT` | `/api/products/{productId}/variants/{variantId}/prices` | Owner, Staff | **[Má»šI]** Cáº­p nháº­t giÃ¡ bÃ¡n sá»‰/láº» biáº¿n thá»ƒ SKU (tÃ¡ch biá»‡t WAC, cháº·n sá»‘ Ã¢m). |
| | `DELETE` | `/api/products/{productId}/variants/{variantId}` | Owner, Staff | **[Má»šI]** VÃ´ hiá»‡u hÃ³a biáº¿n thá»ƒ SKU an toÃ n (Soft delete `IsActive = false`). |
| | `PUT` | `/api/products/{productId}/variants/{variantId}/toggle-status` | Owner, Staff | **[Má»šI]** Báº­t/Táº¯t tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng cá»§a biáº¿n thá»ƒ SKU. |
| **Quáº§y POS** | `GET` | `/api/pos/search` | Authenticated | **[Má»šI]** TÃ¬m kiáº¿m siÃªu tá»‘c theo TÃªn/SKU/Barcode (< 200ms), há»— trá»£ `branchId` tráº£ tá»“n kháº£ dá»¥ng. |
| | `GET` | `/api/pos/barcode/{code}` | Authenticated | **[Má»šI]** QuÃ©t mÃ£ váº¡ch Barcode Scanner, há»— trá»£ `branchId` tráº£ tá»“n kháº£ dá»¥ng. |
| **Chi nhÃ¡nh** | `GET` | `/api/branches` | Authenticated | Láº¥y danh sÃ¡ch chi nhÃ¡nh cá»­a hÃ ng. |
| | `GET` | `/api/branches/{id}` | Authenticated | **[Má»šI]** Láº¥y thÃ´ng tin chi tiáº¿t má»™t chi nhÃ¡nh theo Id. |
| | `POST` | `/api/branches` | Owner | Táº¡o chi nhÃ¡nh má»›i. |
| | `PUT` | `/api/branches/{id}` | Owner | **[Má»šI]** Cáº­p nháº­t thÃ´ng tin chi nhÃ¡nh (TÃªn, Äá»‹a chá»‰, SÄT). |
| | `PUT` | `/api/branches/{id}/toggle-status` | Owner | Báº­t/Táº¯t tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng cá»§a chi nhÃ¡nh. |
| **Kho hÃ ng** | `GET` | `/api/warehouses` | Authenticated | Láº¥y danh sÃ¡ch kho hÃ ng (há»— trá»£ lá»c theo `branchId`). |
| | `GET` | `/api/warehouses/{id}` | Authenticated | **[Má»šI]** Láº¥y thÃ´ng tin chi tiáº¿t má»™t kho hÃ ng theo Id. |
| | `POST` | `/api/warehouses` | Owner, Staff | Táº¡o kho hÃ ng má»›i gáº¯n vá»›i chi nhÃ¡nh. |
| | `PUT` | `/api/warehouses/{id}` | Owner, Staff | **[Má»šI]** Cáº­p nháº­t thÃ´ng tin kho hÃ ng (kiá»ƒm tra `BranchId` cÃ¹ng tenant). |
| | `PUT` | `/api/warehouses/{id}/toggle-status` | Owner, Staff | Báº­t/Táº¯t tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng cá»§a kho hÃ ng. |
| **NhÃ  cung cáº¥p** | `GET` | `/api/suppliers` | Authenticated | Láº¥y danh sÃ¡ch nhÃ  cung cáº¥p (há»— trá»£ tÃ¬m kiáº¿m theo tÃªn, mÃ£, sÄ‘t). |
| | `GET` | `/api/suppliers/{id}` | Authenticated | **[Má»šI]** Láº¥y thÃ´ng tin chi tiáº¿t nhÃ  cung cáº¥p theo Id. |
| | `POST` | `/api/suppliers` | Owner, Staff | Táº¡o má»›i nhÃ  cung cáº¥p hÃ ng hÃ³a. |

---

## 4. Há»¢P Äá»’NG API & Dá»® LIá»†U MáºªU DÃ€NH CHO FRONTEND

### 4.1. Cáº­p nháº­t Biáº¿n thá»ƒ SKU (`PUT /api/products/{productId}/variants/{variantId}`)
- **Header:** `Authorization: Bearer <token>`
- **Request Body máº«u (Há»— trá»£ Object, Array, Number, Boolean, String vÃ  Null lá»“ng nhau):**
```json
{
  "sku": "AO-POLO-DEN-XL",
  "variantName": "Ão Polo Äen Size XL",
  "barcode": "8938501234567",
  "retailPrice": 290000,
  "wholesalePrice": 210000,
  "color": "Äen",
  "size": "XL",
  "attributes": {
    "material": "CVC Cotton",
    "collar": "Bo dá»‡t",
    "season": "Summer 2026",
    "count": 10,
    "waterproof": false,
    "positions": ["S", null, "M"],
    "dimensions": {
      "width": 10,
      "height": 20,
      "depth": null
    }
  },
  "isActive": true
}
```
- **Response 200 OK máº«u (Báº£o toÃ n 100% kiá»ƒu JSON vÃ  null lá»“ng nhau):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "productId": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
  "sku": "AO-POLO-DEN-XL",
  "variantName": "Ão Polo Äen Size XL",
  "barcode": "8938501234567",
  "retailPrice": 290000,
  "wholesalePrice": 210000,
  "costPrice": 0,
  "color": "Äen",
  "size": "XL",
  "attributes": {
    "color": "Äen",
    "size": "XL",
    "material": "CVC Cotton",
    "collar": "Bo dá»‡t",
    "season": "Summer 2026",
    "count": 10,
    "waterproof": false,
    "positions": ["S", null, "M"],
    "dimensions": {
      "width": 10,
      "height": 20,
      "depth": null
    }
  },
  "isActive": true
}
```

### 4.2. Cáº­p nháº­t Báº£ng giÃ¡ SKU (`PUT /api/products/{productId}/variants/{variantId}/prices`)
- **Header:** `Authorization: Bearer <token>` (Roles: Owner, Staff)
- **Request Body máº«u:**
```json
{
  "retailPrice": 320000,
  "wholesalePrice": 240000
}
```
- **Response 200 OK:** Tráº£ vá» Ä‘á»‘i tÆ°á»£ng `ProductVariantResponse` vá»›i giÃ¡ má»›i cáº­p nháº­t.

### 4.3. QuÃ©t Barcode & TÃ¬m kiáº¿m POS kÃ¨m Tá»“n kho (`GET /api/pos/search?query=polo&branchId=...`)
- **Response 200 OK máº«u:**
```json
[
  {
    "variantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "productId": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
    "sku": "AO-POLO-DEN-XL",
    "productName": "Ão Polo Nam",
    "variantName": "Ão Polo Äen Size XL",
    "barcode": "8938501234567",
    "retailPrice": 290000,
    "color": "Äen",
    "size": "XL",
    "attributes": {
      "material": "CVC Cotton",
      "positions": ["S", null, "M"],
      "dimensions": {
        "width": 10,
        "depth": null
      }
    },
    "onHand": 100.0,
    "reserved": 25.0,
    "available": 75.0
  }
]
```

### 4.4. MÃ£ lá»—i HTTP chuáº©n má»±c
- `400 Bad Request`: Dá»¯ liá»‡u Ä‘áº§u vÃ o sai validation, danh má»¥c cha khÃ´ng tá»“n táº¡i hoáº·c vÃ²ng láº·p quan há»‡.
- `401 Unauthorized`: Sai máº­t kháº©u, token háº¿t háº¡n, hoáº·c User/Tenant bá»‹ táº¡m ngÆ°ng/khÃ³a.
- `403 Forbidden`: NgÆ°á»i dÃ¹ng khÃ´ng cÃ³ vai trÃ² phÃ¹ há»£p (vÃ­ dá»¥: Cashier gá»i API quáº£n lÃ½ chi nhÃ¡nh, kho hÃ ng hoáº·c nháº­p kho).
- `404 Not Found`: KhÃ´ng tÃ¬m tháº¥y tÃ i nguyÃªn theo Id hoáº·c tÃ i nguyÃªn thuá»™c tenant khÃ¡c.
- `409 Conflict`: TrÃ¹ng email toÃ n cá»¥c, trÃ¹ng mÃ£ SKU trong cÃ¹ng tenant, trÃ¹ng tá»• há»£p thuá»™c tÃ­nh SKU trong sáº£n pháº©m, hoáº·c Ä‘á»•i chi nhÃ¡nh kho Ä‘Ã£ cÃ³ lá»‹ch sá»­.

---

## 5. HÆ¯á»šNG DáºªN Cáº¤U HÃŒNH & CHáº Y á»¨NG Dá»¤NG / KIá»‚M THá»¬

### 5.1. Khá»Ÿi Ä‘á»™ng mÃ´i trÆ°á»ng PostgreSQL
Dá»± Ã¡n sá»­ dá»¥ng PostgreSQL 16+ tháº­t (khá»Ÿi cháº¡y qua Docker Compose):
```powershell
docker compose up -d postgres
```

### 5.2. Cháº¡y á»©ng dá»¥ng Web API
```powershell
cd src/backend/src/OISM.WebApi
dotnet run
```
Truy cáº­p Swagger UI táº¡i: `http://localhost:5000/swagger`

### 5.3. Thá»±c thi Bá»™ Kiá»ƒm thá»­ Tá»± Ä‘á»™ng
```powershell
# 1. Cháº¡y toÃ n bá»™ 99 Unit Tests (Validate DTOs, PhoneHelper E.164, Barcode EAN-13, Thuá»™c tÃ­nh Ä‘á»™ng, Báº£o toÃ n kiá»ƒu JSON & Null lá»“ng nhau):
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj

# 2. Cháº¡y bá»™ kiá»ƒm thá»­ há»“i quy Backend 2 trÃªn PostgreSQL tháº­t:
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~PostgresBackend2RegressionTests"

# 3. Cháº¡y toÃ n bá»™ Solution (188 tests):
dotnet test src/backend/OISM.sln
```

---

## 6. Báº°NG CHá»¨NG KIá»‚M THá»¬ THá»°C Táº¾ (TEST RESULTS)

| PhÃ¢n há»‡ kiá»ƒm thá»­ | Sá»‘ bÃ i kiá»ƒm thá»­ | Tráº¡ng thÃ¡i | Thá»i gian thá»±c thi | MÃ´i trÆ°á»ng kiá»ƒm thá»­ |
| :--- | :---: | :---: | :---: | :--- |
| **`OISM.UnitTests`** | **99 / 99** | **PASSED 100%** | 115 ms | .NET Core 8.0 x64 |
| **`OISM.IntegrationTests`** | **89 / 89** | **PASSED 100%** | 3m 21s | PostgreSQL 16+ tháº­t (`oisms_test`) |
| **Tá»•ng cá»™ng toÃ n giáº£i phÃ¡p** | **188 / 188** | **PASSED 100%** | 3m 22s | **0 Failed, 0 Skipped, 0 Warning, 0 Error** |

> [!IMPORTANT]
> **PhÃ¢n Ä‘á»‹nh rÃµ pháº¡m vi kiá»ƒm thá»­ & Giá»›i háº¡n kiá»ƒm chá»©ng hiá»‡n táº¡i:**
> - ToÃ n bá»™ 188 bÃ i test Ä‘áº¡t 100% (99 Unit Tests + 89 Integration Tests) lÃ  cÃ¡c kiá»ƒm thá»­ tá»± Ä‘á»™ng táº§ng Backend Web API qua giao thá»©c HTTP (`WebApplicationFactoryWithPostgres`) vÃ  trá»±c tiáº¿p trÃªn cÆ¡ sá»Ÿ dá»¯ liá»‡u PostgreSQL tháº­t (`oisms_test`).
> - Kiá»ƒm thá»­ Backend qua HTTP API **khÃ´ng thay tháº¿** cho kiá»ƒm thá»­ End-to-End (E2E) trá»±c quan qua giao diá»‡n ngÆ°á»i dÃ¹ng Frontend (UI).
> - **Giá»›i háº¡n kiá»ƒm chá»©ng hiá»‡n táº¡i:**
>   1. **ChÆ°a xÃ¡c minh CI trÃªn working tree hiá»‡n táº¡i:** ToÃ n bá»™ quÃ¡ trÃ¬nh kiá»ƒm thá»­, build vÃ  Ä‘o coverage Ä‘Æ°á»£c thá»±c hiá»‡n vÃ  kiá»ƒm chá»©ng thá»±c táº¿ táº¡i local working tree trÃªn mÃ´i trÆ°á»ng mÃ¡y phÃ¡t triá»ƒn; chÆ°a cháº¡y pipeline CI tá»« xa cho commit má»›i nÃ y.
>   2. **ChÆ°a xÃ¡c minh E2E qua giao diá»‡n Frontend:** Cáº§n Ä‘á»™i ngÅ© Frontend vÃ  QA/Tester thá»±c hiá»‡n nghiá»‡m thu liÃªn thÃ´ng giá»¯a mÃ n hÃ¬nh giao diá»‡n ngÆ°á»i dÃ¹ng vÃ  API Backend.

---

## 7. Káº¾T LUáº¬N & BÃ€N GIAO TIáº¾P THEO

ToÃ n bá»™ cÃ¡c nhiá»‡m vá»¥ cá»§a **ThÃ nh viÃªn 2 (Nhá»±t) tá»« BE2-01 Ä‘áº¿n BE2-09** Ä‘Ã£ Ä‘Æ°á»£c hoÃ n thiá»‡n triá»‡t Ä‘á»ƒ:
1. ToÃ n bá»™ lá»—i review (Cáº­p nháº­t Category Ä‘á»“ng thá»i táº¡o vÃ²ng láº·p, Tenant khÃ³a refresh, Race condition token rotation, Update category thiáº¿u parentId, Cháº·n trÃ¹ng tá»• há»£p SKU, Äá»•i chi nhÃ¡nh kho cÃ³ lá»‹ch sá»­, Máº¥t kiá»ƒu JSON vÃ  Loáº¡i bá» null lá»“ng nhau, Chuáº©n hÃ³a sá»‘ Ä‘iá»‡n thoáº¡i E.164 chá»‘ng lá»—i 500, SKU Barcode trÃ¹ng chá»‘ng lá»—i 500) Ä‘Ã£ Ä‘Æ°á»£c giáº£i quyáº¿t táº­n gá»‘c vÃ  chá»©ng minh qua bÃ i kiá»ƒm thá»­ PostgreSQL tháº­t.
2. Táº¥t cáº£ cÃ¡c chá»©c nÄƒng cÃ²n thiáº¿u (CRUD SKU, sá»­a giÃ¡ sá»‰/láº», thuá»™c tÃ­nh JSON Ä‘á»™ng báº£o toÃ n kiá»ƒu vÃ  null, tá»“n kháº£ dá»¥ng POS, sá»­a chi nhÃ¡nh/kho) Ä‘Ã£ Ä‘Æ°á»£c triá»ƒn khai vÃ  kiá»ƒm chá»©ng Ä‘áº§y Ä‘á»§.
3. Há»£p Ä‘á»“ng API Swagger Ä‘Ã£ hoÃ n chá»‰nh, sáºµn sÃ ng Ä‘á»ƒ Frontend (Admin cá»§a PhÃºc Há»¯u vÃ  POS cá»§a PhÆ°Æ¡ng) káº¿t ná»‘i trá»±c tiáº¿p trong Sprint 2 & 3.

