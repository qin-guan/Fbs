<script setup lang="ts">
useHead({
  title: '3SIB Facility Booking',
})
</script>

<template>
  <Toast />
  <NuxtRouteAnnouncer />
  <NuxtLoadingIndicator />
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
</template>

<style>
#__nuxt {
  height: 100vh;
}

body {
  font-family: Inter, sans-serif;
}
</style>
