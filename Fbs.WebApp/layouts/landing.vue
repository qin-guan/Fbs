<script setup lang="ts">
const { data: me } = useMe()
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
            src="/images/logo.png"
            alt="3SIB crest"
            width="32"
            height="26"
            class="h-7 w-auto"
          >
          <span>3SIB Facility Bookings</span>
        </NuxtLink>

        <div class="flex items-center gap-1.5">
          <UColorModeButton />

          <UButton
            v-if="me?.phone"
            to="/booking"
            label="Dashboard"
            trailing-icon="i-lucide-arrow-right"
          />
          <UButton
            v-else
            to="/auth/login"
            label="Login"
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
