<script setup lang="ts">
definePageMeta({
  layout: 'app',
})
</script>

<template>
  <div class="flex h-full">
    <div class="border-r-1 border-r-gray-200 px-2 py-4 space-y-4">
      <div>
        <span class="font-semibold">3SIB Facility Booking</span>
      </div>

      <UNavigationMenu
        orientation="vertical"
        :items="[
          {
            label: 'Bookings',
            to: '/booking',
          },
          {
            label: 'Help',
            target: '_blank',
            to: 'https://www.sos.org.sg/contact-us/',
          },
        ]"
        class="data-[orientation=vertical]:w-48"
      />
    </div>

    <div class="flex flex-1">
      <NuxtPage />
    </div>
  </div>
</template>
