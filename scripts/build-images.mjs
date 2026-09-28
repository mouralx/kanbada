import { spawnSync } from 'node:child_process';

const engine = process.argv[2] || 'podman';
const images = [
  ['api', 'tools/images/api.Dockerfile'],
  ['portal', 'tools/images/portal.Dockerfile'],
  ['worker', 'tools/images/worker.Dockerfile'],
];

for (const [name, dockerfile] of images) {
  const tag = `ghcr.io/mouralx/kanbada-${name}:latest`;
  const result = spawnSync(engine, ['build', '--tag', tag, '--file', dockerfile, '.'], { stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
