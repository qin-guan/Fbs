<template>
  <div class="min-h-svh flex flex-col bg-default">
    <header class="sticky top-0 z-10 border-b border-default bg-default/75 backdrop-blur">
      <UContainer class="h-(--ui-header-height) flex items-center justify-between gap-3">
        <NuxtLink
          to="/"
          class="flex items-center gap-2.5 font-semibold text-highlighted"
        >
          <UIcon
            name="i-lucide-calendar-check"
            class="size-6 text-primary"
          />
          <span>Facility Booking</span>
        </NuxtLink>

        <div class="flex items-center gap-1.5">
          <UButton
            to="/orgs"
            label="Organizations"
            icon="i-lucide-building-2"
            color="neutral"
            variant="ghost"
          />
          <UColorModeButton />
          <AccountMenu />
        </div>
      </UContainer>
    </header>

    <main class="flex flex-1 flex-col">
      <UContainer class="w-full max-w-2xl py-8">
        <slot />
      </UContainer>
    </main>
  </div>
</template>
