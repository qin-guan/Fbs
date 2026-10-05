<script setup lang="ts">
definePageMeta({
  layout: 'landing',
})

// The old phone number sign in has its own home page, until the switch to accounts
const accounts = usesAccounts()
</script>

<template>
  <HomeLegacy v-if="!accounts" />
  <HomeAccounts v-else />
</template>
