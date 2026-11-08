# OISMS Admin Dashboard

Ứng dụng quản trị tại `http://localhost:5173/`. Quầy bán hàng hiện nằm trong `../pos-app` tại `http://localhost:5174/`; nút POS và đường dẫn Admin `/pos` chuyển sang ứng dụng đó. Không còn implementation quầy trong Admin.

Chạy `npm ci`, `npm run dev`; build bằng `npm run build`. Cấu hình `VITE_API_BASE_URL` trỏ API (local `http://localhost:5000`) và `VITE_POS_URL` trỏ URL POS. Các biến Vite được gắn khi build Docker. Admin và POS dùng phiên riêng; Cashier được chuyển sang POS và không giữ phiên Admin.

Phần dưới là ghi chú công cụ của template frontend.

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend enabling type-aware lint rules by installing `oxlint-tsgolint` and editing `.oxlintrc.json`:

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript", "oxc"],
  "options": {
    "typeAware": true
  },
  "rules": {
    "react/rules-of-hooks": "error",
    "react/only-export-components": ["warn", { "allowConstantExport": true }]
  }
}
```

See the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules) for the full list of rules and categories.
