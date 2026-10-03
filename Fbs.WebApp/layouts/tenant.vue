<script setup lang="ts">
const { org, error, isPending, slug } = useTenant()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

// Where they were last is where they go next time
watch(org, (value) => {
  if (value) {
    lastOrganization.value = slug.value
    useHead({ titleTemplate: `%s | ${value.name}` })
  }
})
</script>

<template>
  <UDashboardGroup unit="rem">
    <TenantSidebar v-if="org" />

    <div
      v-if="isPending"
      class="flex flex-1 items-center justify-center text-muted"
    >
      <UIcon
        name="i-lucide-loader-circle"
        class="size-6 animate-spin"
      />
    </div>
    <TenantUnavailable
      v-else-if="error"
      :error="error"
    />
    <slot v-else />
  </UDashboardGroup>
</template>
