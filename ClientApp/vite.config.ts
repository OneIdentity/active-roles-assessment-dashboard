import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  // Relative base so the bundle works when the site is hosted under an IIS sub-application
  // (e.g. /ActiveRolesDashboard/app/) as well as at the server root.
  base: './',
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
