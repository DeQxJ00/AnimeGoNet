import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "tests/web-e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: true,
  retries: 0,
  timeout: 30_000,
  expect: { timeout: 15_000 },
  outputDir: ".artifacts/playwright-results",
  reporter: [
    ["line"],
    ["html", { outputFolder: ".artifacts/playwright-report", open: "never" }],
  ],
  use: {
    browserName: "chromium",
    headless: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "off",
  },
});
