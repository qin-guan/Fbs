<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const route = useRoute()
const { visible } = useSidebar()
const { org, isAdmin, path } = useTenant()

function close() {
  visible.value = false
}

const links = computed<NavigationMenuItem[][]>(() => [
  [
    {
      label: 'Bookings',
      icon: 'i-lucide-calendar-days',
      to: path(),
      active: route.path === path() || route.path.startsWith(`${path('bookings')}/`),
      onSelect: close,
    },
    {
      label: 'Timeline',
      icon: 'i-lucide-chart-gantt',
      to: path('timeline'),
      onSelect: close,
    },
    {
      label: 'New booking',
      icon: 'i-lucide-calendar-plus',
      to: path('book'),
      active: route.path === path('book') || route.path.startsWith(`${path('book')}/`),
      onSelect: close,
    },
  ],
  ...(isAdmin.value
    ? [[
        { label: 'Manage', type: 'label' as const },
        { label: 'People', icon: 'i-lucide-users-round', to: path('admin', 'members'), onSelect: close },
        { label: 'Facilities', icon: 'i-lucide-building-2', to: path('admin', 'facilities'), onSelect: close },
        { label: 'Units', icon: 'i-lucide-layers', to: path('admin', 'units'), onSelect: close },
        { label: 'Invite links', icon: 'i-lucide-link', to: path('admin', 'invites'), onSelect: close },
        { label: 'Settings', icon: 'i-lucide-settings', to: path('admin', 'settings'), onSelect: close },
        { label: 'History', icon: 'i-lucide-history', to: path('admin', 'audit'), onSelect: close },
      ]]
    : []),
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
        :to="path()"
        class="flex items-center gap-2.5 min-w-0"
        :class="{ 'mx-auto': collapsed }"
      >
        <UIcon
          name="i-lucide-calendar-check"
          class="size-6 shrink-0 text-primary"
        />
        <span
          v-if="!collapsed"
          class="truncate text-sm font-semibold text-highlighted"
        >
          {{ org?.name ?? 'Loading...' }}
        </span>
      </NuxtLink>
    </template>

    <template #default="{ collapsed }">
      <UNavigationMenu
        v-for="(group, index) in links"
        :key="index"
        :collapsed="collapsed"
        :items="collapsed ? group.filter(link => link.type !== 'label') : group"
        orientation="vertical"
        tooltip
      />

      <UNavigationMenu
        :collapsed="collapsed"
        :items="[[
          { label: 'Switch organization', icon: 'i-lucide-arrow-left-right', to: '/orgs', onSelect: close },
          { label: 'Your account', icon: 'i-lucide-circle-user-round', to: '/account', onSelect: close },
        ]]"
        orientation="vertical"
        tooltip
        class="mt-auto"
      />
    </template>

    <template #footer="{ collapsed }">
      <div
        class="flex w-full items-center gap-2"
        :class="collapsed ? 'flex-col' : ''"
      >
        <div
          v-if="!collapsed"
          class="min-w-0 flex-1"
        >
          <span class="block truncate text-sm font-medium text-highlighted">{{ org?.me.displayName }}</span>
          <span class="block truncate text-xs text-muted">{{ isAdmin ? 'Admin' : 'Member' }}</span>
        </div>

        <UColorModeButton />
        <AccountMenu />
      </div>
    </template>
  </UDashboardSidebar>
</template>
