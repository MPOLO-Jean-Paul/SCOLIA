import { defineConfig } from '@playwright/test';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// Tests de bout en bout : le vrai serveur SCOLIA (.NET) est lancé sur une base temporaire vide.
const dataDirectory = mkdtempSync(join(tmpdir(), 'scolia-e2e-'));
const port = Number(process.env.SCOLIA_E2E_PORT ?? 5199);

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: `http://localhost:${port}`,
    locale: 'fr-FR',
    viewport: { width: 1440, height: 900 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    launchOptions: process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
  },
  webServer: {
    command: `dotnet run --no-launch-profile --project ../src/Scolia.Api -c Release -f net10.0`,
    url: `http://localhost:${port}/api/sante`,
    timeout: 180_000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      Scolia__Stockage__DataDirectory: dataDirectory,
      Scolia__Reseau__Port: String(port),
      Scolia__OuvrirNavigateur: 'false',
    },
  },
});
