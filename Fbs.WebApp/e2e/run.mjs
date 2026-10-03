// Builds the app for the browser tests (NUXT_PUBLIC_AUTH_MODE=test), serves it, runs the tests in e2e/ against it, and stops it.
// Pass --no-build to use what is in .output already, which must have been built that way.
import { spawn, spawnSync } from 'node:child_process'
import { setTimeout as sleep } from 'node:timers/promises'

const port = process.env.PORT ?? '3123'
const env = { ...process.env, NUXT_PUBLIC_AUTH_MODE: 'test', PORT: port }
delete env.NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY

if (!process.argv.includes('--no-build')) {
  const build = spawnSync('npx', ['nuxi', 'build'], { env, stdio: 'inherit' })
  if (build.status !== 0) process.exit(build.status ?? 1)
}

const server = spawn('node', ['.output/server/index.mjs'], { env, stdio: 'inherit' })
try {
  for (let i = 0; i < 60; i++) {
    try {
      if ((await fetch(`http://localhost:${port}/onboarding`)).ok) break
    }
    catch { /* not up yet */ }
    await sleep(500)
  }

  for (const file of ['smoke', 'tenant', 'booking', 'timeline', 'admin-settings', 'admin-units-facilities', 'admin-members', 'admin-invites', 'admin-setup']) {
    console.log(`\n== ${file}`)
    const test = spawnSync('node', [`e2e/${file}.mjs`], { env: { ...env, BASE_URL: `http://localhost:${port}` }, stdio: 'inherit' })
    if (test.status !== 0) {
      process.exitCode = test.status ?? 1
    }
  }
}
finally {
  server.kill()
}
