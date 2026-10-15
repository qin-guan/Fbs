<script setup lang="ts">
import { site } from '~/lib/site'

const route = useRoute()
const { siteUrl } = useRuntimeConfig().public

// The address a page is known by to search engines and link previews: without the query, which only says what is shown on it
const url = computed(() => `${siteUrl}${route.path}`)

useHead({
  title: site.name,
  // A page's own title, and then the name, unless it has it already (the home page's)
  titleTemplate: title => !title || title.includes(site.name) ? title || site.name : `${title} | ${site.name}`,
  link: [{ rel: 'canonical', href: url }],
})

useSeoMeta({
  ogUrl: url,
})
</script>

<template>
  <UApp>
    <NuxtRouteAnnouncer />
    <NuxtLoadingIndicator color="var(--ui-primary)" />
    <NuxtLayout>
      <NuxtPage />
    </NuxtLayout>
  </UApp>
</template>
