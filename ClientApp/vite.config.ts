import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  base: '/app/',
  plugins: [react()],
  build: {
    outDir: '../wwwroot/app',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: 'https://localhost:7168', changeOrigin: true, secure: false },
    },
  },
})
