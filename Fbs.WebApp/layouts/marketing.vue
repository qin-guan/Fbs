<script setup lang="ts">
import { site } from '~/lib/site'

// The home page's: a header with the way round the page, and a footer. Drawn when the app is built (see routeRules in nuxt.config.ts),
// so what depends on who is signed in comes from useHomeLinks, and the banner is only drawn in the browser.
const { primary, header } = useHomeLinks()

const sections = [
  { label: 'Features', to: '/#features' },
  { label: 'Open source', to: '/#open-source' },
  { label: 'How it works', to: '/#how-it-works' },
  { label: 'Use cases', to: '/#use-cases' },
  { label: 'FAQ', to: '/#faq' },
]

const year = new Date().getFullYear()

// Going to a section glides there, unless somebody would rather it didn't
useHead({ htmlAttrs: { class: 'motion-safe:scroll-smooth' } })
</script>

<template>
  <div class="min-h-svh flex flex-col bg-default">
    <UHeader>
      <!-- Instead of the title, which would be a link around the logo's own -->
      <template #left>
        <AppLogo />
      </template>

      <nav aria-label="Sections">
        <ul class="flex items-center gap-1">
          <li
            v-for="section in sections"
            :key="section.to"
          >
            <UButton
              :to="section.to"
              :label="section.label"
              color="neutral"
              variant="ghost"
              active-class=""
            />
          </li>
        </ul>
      </nav>

      <template #right>
        <UButton
          :to="site.repository"
          target="_blank"
          icon="i-simple-icons-github"
          color="neutral"
          variant="ghost"
          aria-label="Book A Space on GitHub"
        />
        <UColorModeButton />
        <UButton
          v-for="link in header"
          :key="link.label"
          v-bind="link"
          class="hidden sm:inline-flex"
        />
      </template>

      <template #body>
        <nav aria-label="Sections">
          <ul class="flex flex-col gap-1">
            <li
              v-for="section in sections"
              :key="section.to"
            >
              <UButton
                :to="section.to"
                :label="section.label"
                color="neutral"
                variant="ghost"
                size="lg"
                block
                class="justify-start"
                active-class=""
              />
            </li>
          </ul>
        </nav>

        <div class="mt-6 flex flex-col gap-2 border-t border-default pt-6">
          <UButton
            v-for="link in primary"
            :key="link.label"
            v-bind="link"
            size="lg"
            block
          />
        </div>
      </template>
    </UHeader>

    <!-- What glows behind the pictures reaches past the edges, and mustn't make the page scroll sideways -->
    <main class="flex flex-col flex-1 overflow-x-clip">
      <ClientOnly>
        <UContainer
          v-if="$growthbook.isOn('banner')"
          class="pt-4"
        >
          <UAlert
            color="warning"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            :title="$growthbook.getFeatureValue('banner', 'This should not be here!')"
          />
        </UContainer>
      </ClientOnly>

      <slot />
    </main>

    <footer class="border-t border-default">
      <UContainer class="py-12 lg:py-16">
        <div class="grid gap-10 sm:grid-cols-2 lg:grid-cols-4">
          <div class="sm:col-span-2">
            <AppLogo />
            <p class="mt-4 max-w-sm text-sm text-muted">
              Open source facility booking for organizations that share space. Host it yourself, or use the hosted version on its free plan.
            </p>
          </div>

          <nav aria-labelledby="footer-product">
            <h2
              id="footer-product"
              class="text-sm font-semibold text-highlighted"
            >
              Product
            </h2>
            <ul class="mt-4 space-y-3 text-sm">
              <li
                v-for="section in sections"
                :key="section.to"
              >
                <ULink
                  :to="section.to"
                  class="text-muted hover:text-highlighted"
                  active-class=""
                >
                  {{ section.label }}
                </ULink>
              </li>
              <li>
                <ULink
                  :to="site.repository"
                  target="_blank"
                  class="text-muted hover:text-highlighted"
                >
                  Source code on GitHub
                </ULink>
              </li>
            </ul>
          </nav>

          <nav aria-labelledby="footer-start">
            <h2
              id="footer-start"
              class="text-sm font-semibold text-highlighted"
            >
              Get started
            </h2>
            <ul class="mt-4 space-y-3 text-sm">
              <li
                v-for="link in primary"
                :key="link.label"
              >
                <ULink
                  :to="link.to"
                  :target="link.target"
                  class="text-muted hover:text-highlighted"
                >
                  {{ link.label }}
                </ULink>
              </li>
            </ul>
          </nav>
        </div>

        <div class="mt-12 flex flex-col-reverse items-start justify-between gap-4 border-t border-default pt-8 text-sm text-muted sm:flex-row sm:items-center">
          <p>© {{ year }} Book A Space</p>
          <UColorModeSelect class="w-36" />
        </div>
      </UContainer>
    </footer>
  </div>
</template>
