<script setup lang="ts">
const { data: me } = useMe()
</script>

<template>
  <div class="h-full flex flex-col">
    <header class="mx-auto h-12 flex justify-between items-center container px-3">
      <div>
        <NuxtLink to="/">3SIB Facility Bookings</NuxtLink>
      </div>

      <div>
        <Button
          v-slot="slotProps"
          as-child
          link
        >
          <NuxtLink
            v-if="me?.phone"
            to="/booking"
            :class="slotProps.class"
          >
            Dashboard
          </NuxtLink>

          <NuxtLink
            v-else
            to="/auth/login"
            :class="slotProps.class"
          >
            Login
          </NuxtLink>
        </Button>
      </div>
    </header>

    <main class="flex flex-col flex-1">
      <slot />
    </main>
  </div>
</template>
