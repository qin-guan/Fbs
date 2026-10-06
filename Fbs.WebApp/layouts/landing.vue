<script setup lang="ts">
import { useGetAuthMe } from '~/api'

// The old phone number sign in, until the switch to accounts
const accounts = usesAccounts()
const { data: me } = useGetAuthMe({ query: { enabled: !accounts } })
const { isLoaded, isSignedIn } = useAccountSession()
const signedIn = computed(() => accounts ? isLoaded.value && isSignedIn.value === true : !!me.value?.phone)
</script>

<template>
  <div class="min-h-svh flex flex-col bg-default">
    <header class="sticky top-0 z-10 border-b border-default bg-default/75 backdrop-blur">
      <UContainer class="h-(--ui-header-height) flex items-center justify-between gap-3">
        <NuxtLink
          to="/"
          class="flex items-center gap-2.5 font-semibold text-highlighted"
        >
          <img
            v-if="!accounts"
            src="/images/logo.png"
            alt="3SIB crest"
            width="32"
            height="26"
            class="h-7 w-auto"
          >
          <UIcon
            v-else
            name="i-lucide-calendar-check"
            class="size-6 text-primary"
          />
          <span>Book A Space</span>
        </NuxtLink>

        <div class="flex items-center gap-1.5">
          <UColorModeButton />

          <UButton
            v-if="signedIn"
            :to="accounts ? '/' : '/booking'"
            label="Dashboard"
            trailing-icon="i-lucide-arrow-right"
          />
          <UButton
            v-else
            :to="accounts ? '/sign-in' : '/auth/login'"
            :label="accounts ? 'Sign in' : 'Login'"
            color="neutral"
            variant="outline"
          />
        </div>
      </UContainer>
    </header>

    <main class="flex flex-col flex-1">
      <UContainer
        v-if="$growthbook.isOn('banner')"
        class="pt-4"
      >
        <UAlert
          color="warning"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          :title="$growthbook.getFeatureValue('banner', 'This should not be here!')"
        />
      </UContainer>

      <div class="flex flex-1">
        <slot />
      </div>
    </main>
  </div>
</template>
