import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const memMigrateVersion =
  process.env.MEM_MIGRATE_RELEASE_VERSION?.trim()
  || process.env.npm_package_version?.trim()
  || 'development'

export default defineConfig({
  define: {
    __MEM_MIGRATE_VERSION__: JSON.stringify(memMigrateVersion),
  },
  plugins: [react()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': 'http://127.0.0.1:7391',
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test-setup.ts',
    css: true,
  },
})
