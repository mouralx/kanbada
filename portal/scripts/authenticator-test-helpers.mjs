import { createHmac } from 'node:crypto';
import assert from 'node:assert/strict';

export function authenticatorCode(secret, timestamp = Date.now()) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...secret.replace(/=/g, '')]
    .map((character) => alphabet.indexOf(character).toString(2).padStart(5, '0'))
    .join('');
  const key = Buffer.from(bits.match(/.{8}/g).map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(timestamp / 30000)));
  const digest = createHmac('sha1', key).update(counter).digest();
  return String((digest.readUInt32BE(digest[19] & 15) & 0x7fffffff) % 1000000).padStart(6, '0');
}

export const avatarPhoto =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAO0lEQVR4AezXwQkAMAgDwOIw3bV7dS8dwacgF8g/3C9x38/JxhmOAQQIECBAgAABAgQIECBAYL9Ad74LAAD//6p1CZ0AAAAGSURBVAMAUaROge/nJygAAAAASUVORK5CYII=';

export async function enrollApiAccount(request, password) {
  const headers = { 'X-Kanbada-Request': '1' };
  const avatar = await request.post('http://localhost:4173/api/auth/avatar', {
    headers,
    data: { photo: avatarPhoto },
  });
  assert.equal(avatar.status(), 204);
  const setup = await request.post('http://localhost:4173/api/auth/two-factor/setup', {
    headers,
    data: { password },
  });
  assert.equal(setup.status(), 200);
  const { secret } = await setup.json();
  const confirmation = await request.post('http://localhost:4173/api/auth/two-factor/confirm', {
    headers,
    data: { password, code: authenticatorCode(secret) },
  });
  assert.equal(confirmation.status(), 200);
  return confirmation.json();
}
