<script setup lang="ts">
useHead({
  title: usesAccounts() ? 'Facility Booking' : '3SIB Facility Booking',
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
