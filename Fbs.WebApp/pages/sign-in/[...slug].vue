<script setup lang="ts">
import { SignIn } from '@clerk/vue'

definePageMeta({
  layout: 'landing',
})

useHead({ title: 'Sign in' })

const mode = useAuthMode()
</script>

<template>
  <div class="flex flex-1 items-center justify-center p-4">
    <SignIn
      v-if="mode === 'clerk'"
      routing="path"
      path="/sign-in"
      sign-up-url="/sign-up"
      fallback-redirect-url="/"
    />
    <UAlert
      v-else
      title="Sign in isn't set up for this build"
      description="This is a test build, where nobody needs to sign in."
      color="neutral"
      variant="subtle"
    />
  </div>
</template>
