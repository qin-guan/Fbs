<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'
import { useGetAuthMe } from '~/api'

const route = useRoute()
const { visible } = useSidebar()
const { data: me } = useGetAuthMe()

function close() {
  visible.value = false
}

const links = computed<NavigationMenuItem[][]>(() => [
  [
    {
      label: 'Bookings',
      icon: 'i-lucide-calendar-days',
      to: '/booking',
      active: route.path === '/booking' || (route.path.startsWith('/booking/') && !route.path.startsWith('/booking/new')),
      onSelect: close,
    },
    {
      label: 'New booking',
      icon: 'i-lucide-calendar-plus',
      to: '/booking/new',
      active: route.path.startsWith('/booking/new'),
      onSelect: close,
    },
    {
      label: 'My profile',
      icon: 'i-lucide-circle-user-round',
      to: '/profile',
      onSelect: close,
    },
    {
      label: `What's new`,
      icon: 'i-lucide-sparkles',
      to: '/changelog',
      onSelect: close,
    },
  ],
  [
    {
      label: 'Help',
      type: 'label',
    },
    {
      label: 'FAQs',
      icon: 'i-lucide-circle-help',
      to: '/faqs',
      onSelect: close,
    },
    {
      label: 'Contact us',
      icon: 'i-lucide-send',
      to: 'https://t.me/shadypastures',
      target: '_blank',
      onSelect: close,
    },
    {
      label: 'jialat liao',
      icon: 'i-lucide-life-buoy',
      to: 'https://www.cmpb.gov.sg/life-in-ns/saf/where-to-seek-help/',
      target: '_blank',
      onSelect: close,
    },
  ],
])
</script>

<template>
  <UDashboardSidebar
    v-model:open="visible"
    collapsible
    class="bg-elevated/25"
    :ui="{ footer: 'lg:border-t lg:border-default' }"
  >
    <template #header="{ collapsed }">
      <NuxtLink
        to="/booking"
        class="flex items-center gap-2.5 min-w-0"
        :class="{ 'mx-auto': collapsed }"
      >
        <img
          src="/images/logo.png"
          alt="3SIB crest"
          width="32"
          height="26"
          class="h-7 w-auto shrink-0"
        >
        <template v-if="!collapsed">
          <span class="truncate text-sm font-semibold text-highlighted">
            3SIB Facility Booking
          </span>
          <UBadge
            label="Beta"
            size="sm"
            variant="subtle"
          />
        </template>
      </NuxtLink>
    </template>

    <template #default="{ collapsed }">
      <UNavigationMenu
        :collapsed="collapsed"
        :items="links[0]"
        orientation="vertical"
        tooltip
      />

      <UNavigationMenu
        :collapsed="collapsed"
        :items="collapsed ? links[1]!.filter(link => link.type !== 'label') : links[1]"
        orientation="vertical"
        tooltip
        class="mt-auto"
      />
    </template>

    <template #footer="{ collapsed }">
      <div
        class="flex w-full items-center gap-1"
        :class="collapsed ? 'flex-col' : ''"
      >
        <UButton
          to="/profile"
          color="neutral"
          variant="ghost"
          class="min-w-0 flex-1"
          :class="{ 'justify-center': collapsed }"
          :square="collapsed"
          :aria-label="collapsed ? 'My profile' : undefined"
          @click="close"
        >
          <UAvatar
            :alt="me?.name ?? undefined"
            icon="i-lucide-user-round"
            size="xs"
          />
          <span
            v-if="!collapsed"
            class="min-w-0 text-left"
          >
            <span class="block truncate text-sm font-medium text-highlighted">{{ me?.name ?? 'Loading...' }}</span>
            <span class="block truncate text-xs text-muted">{{ me?.phone }}</span>
          </span>
        </UButton>

        <UColorModeButton />
      </div>
    </template>
  </UDashboardSidebar>
</template>
