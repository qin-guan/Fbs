<script setup lang="ts">
// Where WorkOS comes back to after signing in. AuthKit takes the code it brings for a session as the app starts (see
// plugins/workos-session.client.ts), and then goes on to where they were going, so this only shows while it does, or if it couldn't.
definePageMeta({
  layout: 'landing',
})

useHead({ title: 'Signing in' })

const { isLoaded, isSignedIn } = useAccountSession()
</script>

<template>
  <div class="flex flex-1 items-center justify-center p-4">
    <UAlert
      v-if="isLoaded && !isSignedIn"
      title="Couldn't sign you in"
      description="The link may have been used already, or opened in another browser."
      color="error"
      variant="subtle"
      :actions="[{ label: 'Sign in again', to: '/sign-in' }]"
    />
    <div
      v-else
      class="flex flex-col items-center gap-3 text-muted"
      role="status"
    >
      <div class="flex items-center gap-2">
        <UIcon
          name="i-lucide-loader-circle"
          class="size-5 animate-spin"
        />
        <span>Signing you in…</span>
      </div>
      <UButton
        v-if="isLoaded"
        to="/"
        label="Go to the home page"
        variant="link"
      />
    </div>
  </div>
</template>
