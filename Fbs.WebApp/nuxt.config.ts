// How people sign in, decided when the app is built:
// - `workos` when NUXT_PUBLIC_WORKOS_CLIENT_ID is set: accounts with WorkOS (AuthKit), and organisations under /t/:slug. It comes
//   before Clerk, so a build with both is a WorkOS one, and building without it again goes back to Clerk
// - `clerk` when NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY is set: accounts with Clerk, until everybody has moved to WorkOS
// - `legacy` otherwise: the phone number and Telegram code, and the old pages, until the switch to accounts
// - `test` is for testing the pages in a browser without either: always signed in, with a token that is never checked
const workOSClientId = process.env['NUXT_PUBLIC_WORKOS_CLIENT_ID']
const clerkPublishableKey = process.env['NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY']
const authMode = process.env['NUXT_PUBLIC_AUTH_MODE'] === 'test'
  ? 'test'
  : workOSClientId ? 'workos' : clerkPublishableKey ? 'clerk' : 'legacy'

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
  ssr: false,

  // There is no server to check a session on: the app is static, and the API checks the token
  ...(authMode === 'clerk' ? { clerk: { skipServerMiddleware: true } } : {}),
  devtools: { enabled: false },

  app: {
    head: {
      title: 'Book A Space',
      meta: [
        {
          name: 'description',
          content: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.',
        },
        { name: 'robots', content: 'index, follow' },
        { name: 'theme-color', content: '#f97316' },
        { property: 'og:site_name', content: 'Book A Space' },
        { property: 'og:type', content: 'website' },
        { property: 'og:title', content: 'Book A Space' },
        { property: 'og:description', content: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.' },
        { property: 'og:image', content: '/images/logo.png' },
        { name: 'twitter:card', content: 'summary_large_image' },
        { name: 'twitter:title', content: 'Book A Space' },
        { name: 'twitter:description', content: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.' },
        { name: 'twitter:image', content: '/images/logo.png' },
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
      // The WorkOS client, and the custom authentication domain (such as auth.example.com) that keeps the session in a cookie
      // in production. Without one, AuthKit keeps it in the browser's storage, which is only for development
      workosClientId: workOSClientId || '',
      workosApiHostname: process.env['NUXT_PUBLIC_WORKOS_API_HOSTNAME'] || '',
    },
  },
  compatibilityDate: '2024-11-01',

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
