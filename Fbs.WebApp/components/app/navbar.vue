<script setup lang="ts">
defineProps<{
  title?: string
}>()
</script>

<template>
  <UDashboardNavbar
    :title="title"
    :ui="{ right: 'gap-2', left: 'min-w-0 flex-1' }"
  >
    <template #leading>
      <UDashboardSidebarCollapse />
    </template>

    <template
      v-if="$slots.title"
      #title
    >
      <slot name="title" />
    </template>

    <template
      v-if="$slots.trailing"
      #trailing
    >
      <slot name="trailing" />
    </template>

    <template
      v-if="$slots.right"
      #right
    >
      <slot name="right" />
    </template>
  </UDashboardNavbar>
</template>
