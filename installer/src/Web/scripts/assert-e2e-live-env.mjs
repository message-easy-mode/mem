const required = [
  "MEM_E2E_BASE_URL",
  "MEM_E2E_SETUP_TOKEN",
]

const missing = required.filter((name) => !process.env[name]?.trim())

if (missing.length > 0) {
  console.error(
    `MEM live browser smoke tests require: ${missing.join(", ")}.\n` +
      "Example:\n" +
      "MEM_E2E_BASE_URL=http://127.0.0.1:7105\n" +
      "MEM_E2E_SETUP_TOKEN=mem_...\n" +
      "npm run test:e2e:live",
  )
  process.exit(1)
}
