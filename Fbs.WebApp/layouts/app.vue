<script setup lang="ts">
import { useQuery } from '@urql/vue'

const items = [
  {
    label: 'Bookings',
    to: '/booking',
  },
  {
    label: 'My profile',
    to: '/profile',
  },
  {
    label: 'Help',
    target: '_blank',
    to: 'https://www.sos.org.sg/contact-us/',
  },
]

const { data: me, fetching } = useQuery({ query: queries.meWithName, variables: {} })

whenever(() => !fetching.value, () => {
  if (!me.value?.me) navigateTo('/login')
}, { immediate: true })
</script>

<template>
  <UApp>
    <UDashboardGroup>
      <UDashboardSidebar
        :ui="{ footer: 'border-0' }"
        collapsible
        resizable
      >
        <template #header>
          <span class="font-semibold">3SIB Facility Booking System</span>
        </template>

        <template #default="{ collapsed }">
          <UNavigationMenu
            :collapsed="collapsed"
            orientation="vertical"
            :items="items"
          />
        </template>

        <template #footer="{ collapsed }">
          <div class="flex flex-col w-full gap-4 mb-4">
            <div>
              <NuxtImg
                src="/images/logo.png"
                class="h-16 mt-3 w-auto"
              />
            </div>

            <UButton
              to="/profile"
              :label="collapsed ? undefined : me?.me?.name ?? ''"
              color="neutral"
              variant="ghost"
              class="w-full"
              :block="collapsed"
            />
          </div>
        </template>
      </UDashboardSidebar>

      <slot />
    </UDashboardGroup>
  </UApp>
</template>
