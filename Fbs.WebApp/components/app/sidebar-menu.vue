<script setup lang="ts">
import type { MenuItem } from 'primevue/menuitem'

const { visible } = useSidebar()

const items: MenuItem[] = [
  {
    label: 'Bookings',
    route: '/booking',
  },
  {
    label: 'New booking',
    route: '/booking/new',
  },
  {
    label: 'My profile',
    route: '/profile',
  },
  {
    label: `What's new`,
    route: '/changelog',
  },
  {
    label: 'Help',
    items: [
      {
        label: 'Contact us',
        target: '_blank',
        route: 'https://t.me/shadypastures',
      },
      {
        label: 'jialat liao',
        target: '_blank',
        route: 'https://www.cmpb.gov.sg/life-in-ns/saf/where-to-seek-help/',
      },
    ],
  },
]
</script>

<template>
  <Menu :model="items">
    <template #start>
      <div class="flex items-center gap-3 p-3">
        <span>
          3SIB Facility Booking
        </span>

        <Badge size="small">
          Beta
        </Badge>
      </div>
    </template>

    <template #item="{ item }">
      <Button
        v-if="item.route || item.url"
        v-slot="slotProps"
        link
        as-child
      >
        <NuxtLink
          :class="slotProps.class"
          :to="item.route || item.url"
          :target="item.target"
          style="justify-content: start;"
          class="w-full"
          @click="visible = false"
        >
          {{ item.label }}
        </NuxtLink>
      </Button>
    </template>
  </Menu>
</template>
