import { privatePaths } from '../../lib/site'

// Written when the app is built (nitro.prerender in nuxt.config.ts). Everything is open to crawlers but the pages behind a sign in.
export default defineEventHandler((event) => {
  const { siteUrl } = useRuntimeConfig(event).public
  setHeader(event, 'Content-Type', 'text/plain; charset=utf-8')

  return [
    'User-agent: *',
    'Allow: /',
    ...privatePaths.map(path => `Disallow: ${path}`),
    '',
    `Sitemap: ${siteUrl}/sitemap.xml`,
    '',
  ].join('\n')
})
