import { defineConfig, devices } from "@playwright/test"

const desktopChrome = { ...devices["Desktop Chrome"] }

/**
 * Browser tests always target a running MEM instance. Runtime-mode truth comes
 * from the server-owned /health/runtime preflight, not from the browser port.
 */
export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: process.env.MEM_E2E_BASE_URL ?? "http://127.0.0.1:5173",
    ignoreHTTPSErrors: true,
    // First-owner bootstrap renders recovery codes. Never retain browser
    // evidence that could capture recovery or authentication material.
    trace: "off",
    screenshot: "off",
    video: "off",
  },
  projects: [
    {
      name: "local-development",
      use: desktopChrome,
    },
    {
      name: "containerized-development",
      use: desktopChrome,
    },
    {
      // Existing deployed smoke tests may target a production-shaped or other
      // explicit non-Vite test origin; they are not the disposable container E2E lane.
      name: "deployed-live",
      use: desktopChrome,
    },
    {
      // Existing explicitly mutating live proofs choose their target through
      // MEM_E2E_BASE_URL and retain a neutral project identity.
      name: "live",
      use: desktopChrome,
    },
  ],
})
