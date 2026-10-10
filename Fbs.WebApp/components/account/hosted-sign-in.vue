<script setup lang="ts">
// What shows for the moment it takes to go to WorkOS's pages to sign in, or if that didn't work
defineProps<{ goingTo: boolean, failed: boolean, label: string }>()
defineEmits<{ retry: [] }>()
</script>

<template>
  <UAlert
    v-if="failed"
    title="Couldn't get to the sign in page"
    description="Check your connection, then try again."
    color="error"
    variant="subtle"
    :actions="[{ label: 'Try again', onClick: () => $emit('retry') }]"
  />
  <div
    v-else
    class="flex items-center gap-2 text-muted"
    role="status"
  >
    <UIcon
      name="i-lucide-loader-circle"
      class="size-5 animate-spin"
    />
    <span>{{ label }}…</span>
  </div>
</template>
