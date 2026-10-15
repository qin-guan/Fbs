import { privatePaths, site } from './lib/site'

// How people sign in, decided when the app is built:
// - `clerk` when NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY is set: accounts with Clerk, and organisations under /t/:slug
// - `legacy` otherwise: the phone number and Telegram code, and the old pages, until the switch to Clerk
// - `test` is for testing the pages in a browser without Clerk: always signed in, with a token that is never checked
const clerkPublishableKey = process.env['NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY']
const authMode = process.env['NUXT_PUBLIC_AUTH_MODE'] === 'test' ? 'test' : clerkPublishableKey ? 'clerk' : 'legacy'

// Where the site is reached, for the addresses given to search engines and link previews: the canonical link, og:url, og:image and the
// sitemap. Set it for a build that is deployed anywhere else, such as staging.
const siteUrl = (process.env['NUXT_PUBLIC_SITE_URL'] || 'https://bookaspace.app').replace(/\/+$/, '')

// The pages behind a sign in are sent with noindex, as well as being left out of robots.txt (server/routes/robots.txt.ts)
const noindex = { headers: { 'X-Robots-Tag': 'noindex, nofollow' } }
const privateRouteRules = Object.fromEntries(privatePaths.flatMap(path => path.endsWith('/')
  ? [[`${path}**`, noindex]]
  : [[path, noindex], [`${path}/**`, noindex]]))

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: [
    // @nuxt/ui must come before @nuxt/content so its styled prose components are used
    '@nuxt/ui',
    '@nuxt/content',
    '@nuxt/eslint',
    '@nuxt/fonts',
    '@nuxt/icon',
    '@nuxt/image',
    '@nuxt/scripts',
    '@nuxt/test-utils',
    '@vueuse/nuxt',
    // Only when there is a key for it, so nothing changes for the old sign in until the switch
    ...(authMode === 'clerk' ? ['@clerk/nuxt'] : []),
  ],
  // Every page is drawn in the browser (see routeRules), but the home page, which is drawn when the app is built
  ssr: true,

  // There is no server to check a session on: the app is static, and the API checks the token
  ...(authMode === 'clerk' ? { clerk: { skipServerMiddleware: true } } : {}),
  devtools: { enabled: false },

  app: {
    head: {
      htmlAttrs: { lang: 'en' },
      title: site.name,
      // What every page starts with, and all that crawlers and link previews that don't run JavaScript see of a page drawn in the browser.
      // The home page says more (pages/index.vue), and app.vue adds the canonical link and og:url for each page.
      meta: [
        { name: 'description', content: site.description },
        { name: 'application-name', content: site.name },
        { name: 'apple-mobile-web-app-title', content: site.name },
        { name: 'theme-color', content: '#f97316' },
        { name: 'format-detection', content: 'telephone=no' },
        { property: 'og:site_name', content: site.name },
        { property: 'og:type', content: 'website' },
        { property: 'og:locale', content: 'en_US' },
        { property: 'og:title', content: site.title },
        { property: 'og:description', content: site.description },
        // Previews need the picture's full address
        { property: 'og:image', content: `${siteUrl}/og-image.png` },
        { property: 'og:image:type', content: 'image/png' },
        { property: 'og:image:width', content: '1200' },
        { property: 'og:image:height', content: '630' },
        { property: 'og:image:alt', content: site.imageAlt },
        { name: 'twitter:card', content: 'summary_large_image' },
        { name: 'twitter:title', content: site.title },
        { name: 'twitter:description', content: site.description },
        { name: 'twitter:image', content: `${siteUrl}/og-image.png` },
        { name: 'twitter:image:alt', content: site.imageAlt },
      ],
      link: [
        // The .ico is for browsers without SVG favicons; its sizes keep Chrome from picking it over the SVG
        { rel: 'icon', href: '/favicon.ico', sizes: '32x32' },
        { rel: 'icon', href: '/favicon.svg', type: 'image/svg+xml' },
        { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' },
        { rel: 'manifest', href: '/site.webmanifest' },
      ],
      script: [
        {
          src: 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-1586897931312395',
          crossorigin: 'anonymous',
          async: true,
        },
        {
          innerHTML: `
              (function(c,l,a,r,i,t,y){
              c[a]=c[a]||function(){(c[a].q=c[a].q||[]).push(arguments)};
              t=l.createElement(r);t.async=1;t.src="https://www.clarity.ms/tag/"+i;
              y=l.getElementsByTagName(r)[0];y.parentNode.insertBefore(t,y);
              })(window, document, "clarity", "script", "ra29xtmq6f");
          `,
          type: 'text/javascript',
        },
      ],
    },
  },

  css: ['driver.js/dist/driver.css', '~/assets/css/main.css'],

  content: {
    experimental: { sqliteConnector: 'native' },
  },

  appConfig: {
    ui: {
      colors: {
        primary: 'orange',
        neutral: 'zinc',
      },
    },
  },

  runtimeConfig: {
    public: {
      api: process.env['services__api__http__0'] || 'https://localhost:5204',
      authMode,
      siteUrl,
    },
  },

  routeRules: {
    // There is no server to check a session on: the app is drawn in the browser, and the API checks the token
    '/**': { ssr: false },
    // But the home page is drawn when the app is built, so what it says is in the HTML for search engines and link previews. What depends
    // on who is signed in is only drawn once it is in the browser (composables/home.ts).
    '/': { ssr: true, prerender: true },
    ...privateRouteRules,
  },
  compatibilityDate: '2024-11-01',

  nitro: {
    prerender: {
      // Written when the app is built, from server/routes
      routes: ['/robots.txt', '/sitemap.xml'],
    },
  },

  eslint: {
    config: {
      stylistic: true,
    },
  },

  fonts: {
    families: [
      { name: 'Inter', provider: 'google' },
    ],
  },

  icon: {
    clientBundle: {
      // Bundle icons used in the app, plus the ones Nuxt UI components use internally,
      // so they render without a round trip to the Iconify API.
      scan: true,
      icons: [
        'lucide:arrow-right',
        'lucide:arrow-up-right',
        'lucide:check',
        'lucide:chevron-down',
        'lucide:chevron-left',
        'lucide:chevron-right',
        'lucide:chevrons-left',
        'lucide:chevrons-right',
        'lucide:circle-alert',
        'lucide:circle-check',
        'lucide:circle-x',
        'lucide:ellipsis',
        'lucide:hash',
        'lucide:info',
        'lucide:loader-circle',
        'lucide:menu',
        'lucide:minus',
        'lucide:monitor',
        'lucide:moon',
        'lucide:panel-left-close',
        'lucide:panel-left-open',
        'lucide:plus',
        'lucide:search',
        'lucide:sun',
        'lucide:triangle-alert',
        'lucide:x',
      ],
    },
  },
})
