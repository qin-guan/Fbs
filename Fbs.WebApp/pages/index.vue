<script setup lang="ts">
definePageMeta({
  layout: 'landing',
})
</script>

<template>
  <UPageHero
    title="Faster facility bookings"
    description="No more WhatsApp chats!"
    :links="[{
      label: 'Get started',
      to: '/login',
    }]"
  />
</template>
