import path from "node:path"
import { defineConfig } from "vitest/config"
import react from "@vitejs/plugin-react"
import tailwindcss from "@tailwindcss/vite"

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  test: {
    // Bound jsdom worker contention without relaxing timeouts or assertions.
    maxWorkers: 2,
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
    css: true,
  },
  server: {
    proxy: {
      "/api": {
        target: "http://127.0.0.1:7105",
        changeOrigin: true,
      },
      "/internal": {
        target: "http://127.0.0.1:7105",
        changeOrigin: true,
      },
      "/health": {
        target: "http://127.0.0.1:7105",
        changeOrigin: true,
      },
    },
  },
})