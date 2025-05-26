<script setup lang="ts">
definePageMeta({
  layout: 'app',
})

const { data: home } = await useAsyncData(() => queryCollection('content').path('/changelog').first())

useSeoMeta({
  title: home.value?.title,
  description: home.value?.description,
})
</script>

<template>
  <div class="h-full flex flex-col">
    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <div class="space-x-3 flex items-center">
            <h2>Changelog</h2>
          </div>
        </div>
      </template>
    </AppNavbar>

    <article class="prose p-3">
      <ContentRenderer
        v-if="home"
        :value="home"
      />
    </article>
  </div>
</template>
