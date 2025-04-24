// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({

  modules: [
    '@nuxt/eslint',
    '@nuxt/fonts',
    '@nuxt/icon',
    '@nuxt/image',
    '@nuxt/scripts',
    '@nuxt/ui-pro',
    '@nuxt/test-utils',
    '@vueuse/nuxt',
  ],
  ssr: false,
  devtools: { enabled: true },

  css: ['~/assets/css/main.css'],

  ui: {
    colorMode: false,
  },

  appConfig: {
    ui: {
      colors: {
        primary: 'orange',
      },
    },
  },

  runtimeConfig: {
    public: {
      api: process.env['services__api__http__0'] || 'https://localhost:5204',
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
})
