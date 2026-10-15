// The home page as search engines and link previews see it: what is served, before any JavaScript runs. The page drawn in the browser is in
// smoke.spec.ts, where somebody signed in is taken from it to where they belong.
import { expect, test } from './support'

const site = 'https://bookaspace.app'

test.describe('the home page, for crawlers', () => {
  test('is served with what it says, and with what search engines and link previews read', async ({ request }) => {
    const response = await request.get('/')
    expect(response.status()).toBe(200)
    const html = await response.text()

    expect(html).toMatch(/<html[^>]* lang="en"/)
    expect(html).toContain('<title>Book A Space: facility and room booking for shared spaces</title>')
    expect(html).toMatch(/<h1[^>]*>[^]*The easy way to book[^]*<\/h1>/)
    expect(html).toContain('Everything you need to run shared spaces')
    // The answers are there while they are closed
    expect(html).toContain('made all at once or not at all')

    expect(html).toContain(`<link rel="canonical" href="${site}/">`)
    expect(html).toContain(`<meta property="og:url" content="${site}/">`)
    expect(html).toContain(`<meta property="og:image" content="${site}/og-image.png">`)
    expect(html).toContain('<meta name="twitter:card" content="summary_large_image">')
    expect(html).toMatch(/<meta name="description" content="[^"]{50,160}">/)
    expect(html).toContain('<meta name="robots" content="index, follow')
    expect(html).toContain('<link rel="manifest" href="/site.webmanifest">')
  })

  test('says what it is about as structured data', async ({ request }) => {
    const html = await (await request.get('/')).text()

    const json = /<script type="application\/ld\+json"[^>]*>(.*?)<\/script>/s.exec(html)?.[1]
    const types = (JSON.parse(json!)['@graph'] as { '@type': string }[]).map(node => node['@type'])
    expect(types).toEqual(expect.arrayContaining(['Organization', 'WebSite', 'WebPage', 'WebApplication', 'FAQPage']))
  })

  test('robots.txt keeps crawlers out of the pages behind a sign in, and points to the sitemap', async ({ request }) => {
    const robots = await (await request.get('/robots.txt')).text()
    expect(robots).toContain('Allow: /\n')
    expect(robots).toContain('Disallow: /t/\n')
    expect(robots).toContain('Disallow: /sign-in\n')
    expect(robots).toContain(`Sitemap: ${site}/sitemap.xml`)

    const sitemap = await request.get('/sitemap.xml')
    expect(sitemap.headers()['content-type']).toContain('xml')
    expect(await sitemap.text()).toContain(`<loc>${site}/</loc>`)
  })

  test('the pages behind a sign in are sent with noindex, and the home page isn\'t', async ({ request }) => {
    for (const path of ['/t/alpha', '/sign-in', '/join/sometoken', '/account']) {
      expect((await request.get(path)).headers()['x-robots-tag'], path).toBe('noindex, nofollow')
    }

    expect((await request.get('/')).headers()['x-robots-tag']).toBeUndefined()
  })
})
