import tailwindcss from '@tailwindcss/vite'
import _Aura from '@primeuix/themes/aura'
import { definePreset } from '@primeuix/themes'

const Aura = definePreset(_Aura, {
  semantic: {
    primary: {
      50: '{orange.50}',
      100: '{orange.100}',
      200: '{orange.200}',
      300: '{orange.300}',
      400: '{orange.400}',
      500: '{orange.500}',
      600: '{orange.600}',
      700: '{orange.700}',
      800: '{orange.800}',
      900: '{orange.900}',
      950: '{orange.950}',
    },
  },
})

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: [
    '@nuxt/content',
    '@nuxt/eslint',
    '@nuxt/fonts',
    '@nuxt/icon',
    '@nuxt/image',
    '@nuxt/scripts',
    '@nuxt/test-utils',
    '@vueuse/nuxt',
    '@primevue/nuxt-module',
  ],
  ssr: false,
  devtools: { enabled: false },

  app: {
    head: {
      script: [
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

  css: ['~/assets/css/main.css', 'driver.js/dist/driver.css'],

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

  vite: {
    plugins: [
      tailwindcss(),
    ],
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

  primevue: {
    options: {
      theme: {
        options: {
          darkModeSelector: 'none',
        },
        preset: Aura,
      },
    },
  },
})
