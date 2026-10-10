<script setup lang="ts">
import { SignUp } from '@clerk/vue'

definePageMeta({
  layout: 'landing',
})

useHead({ title: 'Create an account' })

const mode = useAuthMode()
const { goingTo, failed, retry } = useHostedSignIn('sign-up')
</script>

<template>
  <div class="flex flex-1 items-center justify-center p-4">
    <SignUp
      v-if="mode === 'clerk'"
      routing="path"
      path="/sign-up"
      sign-in-url="/sign-in"
      fallback-redirect-url="/"
    />
    <AccountHostedSignIn
      v-else-if="mode === 'workos'"
      :going-to="goingTo"
      :failed="failed"
      label="Taking you to create an account"
      @retry="retry"
    />
    <UAlert
      v-else
      title="Sign up isn't set up for this build"
      description="This is a test build, where nobody needs to sign in."
      color="neutral"
      variant="subtle"
    />
  </div>
</template>
