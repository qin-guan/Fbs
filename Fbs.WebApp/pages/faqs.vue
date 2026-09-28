<script setup lang="ts">
definePageMeta({
  layout: 'app',
})

const { data: home } = await useAsyncData(() => queryCollection('content').path('/help').first())

useSeoMeta({
  title: home.value?.title,
  description: home.value?.description,
})
</script>

<template>
  <UDashboardPanel id="faqs">
    <template #header>
      <AppNavbar title="FAQs" />
    </template>

    <template #body>
      <article class="w-full lg:max-w-3xl mx-auto">
        <ContentRenderer
          v-if="home"
          :value="home"
        />
      </article>
    </template>
  </UDashboardPanel>
</template>
