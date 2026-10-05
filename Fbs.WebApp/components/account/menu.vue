<script setup lang="ts">
import { UserButton } from '@clerk/vue'

const mode = useAuthMode()
</script>

<template>
  <UserButton
    v-if="mode === 'clerk'"
    after-sign-out-url="/"
  />
  <UAvatar
    v-else
    icon="i-lucide-user-round"
    size="sm"
  />
</template>
