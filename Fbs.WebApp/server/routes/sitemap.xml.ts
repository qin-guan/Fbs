// Written when the app is built (nitro.prerender in nuxt.config.ts). The home page is the only page that isn't behind a sign in.
export default defineEventHandler((event) => {
  const { siteUrl } = useRuntimeConfig(event).public
  setHeader(event, 'Content-Type', 'application/xml; charset=utf-8')

  const pages = ['/']

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ...pages.map(path => `  <url><loc>${siteUrl}${path}</loc></url>`),
    '</urlset>',
    '',
  ].join('\n')
})
