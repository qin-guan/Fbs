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
