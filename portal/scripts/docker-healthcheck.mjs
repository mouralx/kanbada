try {
  const response = await fetch('http://127.0.0.1:4173/api/health/ready', {
    signal: AbortSignal.timeout(4000),
  });
  if (!response.ok) throw new Error(`API readiness returned ${response.status}`);
} catch (error) {
  console.error(error);
  process.exit(1);
}
