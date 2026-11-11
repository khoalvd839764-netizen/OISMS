import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// App gọi API theo đường dẫn tương đối. Khi chạy dev, Vite chuyển /api sang backend
// nên không cần mở thêm CORS; khi deploy, Caddy chuyển /api/webhooks/* trên cùng domain.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5175,
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
