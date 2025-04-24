<script setup lang="ts">
import { useQuery } from '@urql/vue'

const me = useQuery({
  query: queries.me,
  variables: {},
})
</script>

<template>
  <UApp>
    <UHeader title="3SIB Facility Booking">
      <template #right>
        <UButton
          v-if="me.data.value?.me?.phone"
          variant="ghost"
          to="/booking"
        >
          My bookings
        </UButton>
        <UButton
          v-else
          variant="ghost"
          to="/login"
        >
          Login
        </UButton>
      </template>
    </UHeader>

    <UMain class="flex flex-col">
      <slot />
    </UMain>

    <UFooter>
      <template #right>
        <div class="flex gap-6 items-center">
          <span>Built by</span>
          <NuxtImg
            src="/images/logo.png"
            class="h-10 w-auto"
          />
        </div>
      </template>
    </UFooter>
  </UApp>
</template>
