<script setup lang="ts">
import { useQuery } from '@urql/vue'

definePageMeta({
  layout: 'landing',
})

const me = useQuery({
  query: queries.me,
  variables: {},
})

watchEffect(() => {
  if (me.data.value?.me?.phone) {
    navigateTo('/booking')
  }
})
</script>

<template>
  <div class="flex-1 flex items-center justify-center">
    <div
      v-if="me.fetching.value"
      class="space-y-2"
    >
      <USkeleton
        class="w-48 h-8"
      />
      <USkeleton
        class="w-40 h-8"
      />
      <USkeleton
        class="w-48 h-8"
      />
    </div>

    <LoginForm v-else />
  </div>
</template>
