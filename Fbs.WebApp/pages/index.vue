<script setup lang="ts">
definePageMeta({
  layout: 'landing',
})

useSeoMeta({
  title: 'Book A Space',
  description: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.',
  ogTitle: 'Book A Space',
  ogDescription: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.',
  twitterTitle: 'Book A Space',
  twitterDescription: 'Book A Space helps units, clubs, and teams check facility availability, coordinate shared spaces, and manage bookings from a phone.',
})

// The old phone number sign in has its own home page, until the switch to accounts
const accounts = usesAccounts()
</script>

<template>
  <HomeLegacy v-if="!accounts" />
  <HomeAccounts v-else />
</template>
