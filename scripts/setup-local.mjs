import { randomBytes } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Run from any directory. Never replace existing credentials or print secret values.
const api = new URL('../api/', import.meta.url);
const envPath = fileURLToPath(new URL('.postgres.env', api));
const configPath = fileURLToPath(new URL('appsettings.Local.json', api));
const envExists = existsSync(envPath);
const configExists = existsSync(configPath);
if (envExists && configExists) {
  console.log('Local configuration already exists. No files changed.');
} else if (configExists) {
  throw new Error('appsettings.Local.json exists without .postgres.env. Configure the database environment manually to preserve your existing connection.');
} else {
  const password = envExists
    ? readFileSync(envPath, 'utf8').match(/^POSTGRES_PASSWORD=(.+)$/m)?.[1]?.trim()
    : randomBytes(32).toString('hex');
  if (!password || !/^[a-zA-Z0-9_-]+$/.test(password)) {
    throw new Error('Existing database password requires manual connection-string configuration. No files changed.');
  }
  if (!envExists) {
    writeFileSync(envPath, `POSTGRES_USER=kanbada\nPOSTGRES_DB=kanbada\nPOSTGRES_PASSWORD=${password}\n`, { mode: 0o600, flag: 'wx' });
  }
  const config = {
    ConnectionStrings: {
      Postgres: `Host=127.0.0.1;Port=55432;Database=kanbada;Username=kanbada;Password=${password}`,
    },
    PortalOrigin: 'http://localhost:4173',
  };
  writeFileSync(configPath, JSON.stringify(config, null, 2) + '\n', { mode: 0o600, flag: 'wx' });
  console.log('Local PostgreSQL configuration created. Start the database, API, and portal as described in README.md.');
}
