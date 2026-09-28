import { randomBytes } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Run from any directory. Never print secret values or replace existing credentials.
const rootEnvPath = fileURLToPath(new URL('../.env', import.meta.url));
const api = new URL('../api/', import.meta.url);
const legacyEnvPath = fileURLToPath(new URL('.postgres.env', api));
const configPath = fileURLToPath(new URL('appsettings.Local.json', api));
const readEnv = (path) => Object.fromEntries(
  readFileSync(path, 'utf8').split(/\r?\n/)
    .map((line) => line.match(/^([A-Z][A-Z0-9_]*)=(.*)$/))
    .filter(Boolean)
    .map(([, key, value]) => [key, value]),
);
const rootEnvExists = existsSync(rootEnvPath);
const legacyEnvExists = existsSync(legacyEnvPath);
const configExists = existsSync(configPath);
const rootValues = rootEnvExists ? readEnv(rootEnvPath) : {};
const legacyValues = legacyEnvExists ? readEnv(legacyEnvPath) : {};
const existingValues = legacyEnvExists ? legacyValues : rootValues;

if (configExists && !existingValues.POSTGRES_PASSWORD) {
  throw new Error('appsettings.Local.json exists without database credentials. Configure the database environment manually to preserve your existing connection.');
}

const database = {
  POSTGRES_USER: existingValues.POSTGRES_USER || 'kanbada',
  POSTGRES_DB: existingValues.POSTGRES_DB || 'kanbada',
  POSTGRES_PASSWORD: existingValues.POSTGRES_PASSWORD || randomBytes(32).toString('hex'),
};
if (Object.values(database).some((value) => !/^[a-zA-Z0-9_-]+$/.test(value))) {
  throw new Error('Existing database credentials require manual Compose configuration. No files changed.');
}

if (!configExists) {
  const config = {
    ConnectionStrings: {
      Postgres: `Host=127.0.0.1;Port=55432;Database=${database.POSTGRES_DB};Username=${database.POSTGRES_USER};Password=${database.POSTGRES_PASSWORD}`,
    },
    PortalOrigin: 'http://localhost:4173',
  };
  writeFileSync(configPath, JSON.stringify(config, null, 2) + '\n', { mode: 0o600, flag: 'wx' });
}

const rootLines = rootEnvExists ? readFileSync(rootEnvPath, 'utf8').split(/\r?\n/) : [];
for (const [key, value] of Object.entries(database)) {
  const index = rootLines.findIndex((line) => line.startsWith(`${key}=`));
  if (index === -1) rootLines.push(`${key}=${value}`);
  else if (legacyEnvExists) rootLines[index] = `${key}=${value}`;
}
writeFileSync(rootEnvPath, `${rootLines.filter((line, index) => line || index < rootLines.length - 1).join('\n').replace(/\n*$/, '\n')}`, { mode: 0o600 });

console.log(configExists ? 'Local Compose credentials are ready.' : 'Local PostgreSQL configuration created. Start the database, API, and portal as described in README.md.');
