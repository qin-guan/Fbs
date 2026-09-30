<script setup lang="ts">
const props = defineProps<{
  error: unknown
}>()

// What went wrong decides what is said: an organization that isn't there, or that they aren't in, look the same on purpose
const status = computed(() => getErrorStatus(props.error))
const code = computed(() => (getProblemDetails(props.error) as { code?: string } | undefined)?.code)
</script>

<template>
  <div class="flex flex-1 items-center justify-center p-6">
    <UPageCard
      class="max-w-md"
      variant="subtle"
      :icon="code === 'pending' ? 'i-lucide-hourglass' : 'i-lucide-circle-slash'"
      :title="code === 'pending' ? 'Waiting for an admin' : code === 'unavailable' ? 'This organization isn\'t available' : status === 404 ? 'There is no such organization' : 'Something went wrong'"
      :description="code === 'pending'
        ? 'An admin has to let you in first. You will be able to use it as soon as they do.'
        : code === 'unavailable'
          ? 'It has been paused. Ask its admins.'
          : status === 404
            ? 'It may have a different address, or you may not be in it.'
            : 'Try again in a moment.'"
    >
      <UButton
        to="/orgs"
        label="Your organizations"
        color="neutral"
        variant="subtle"
      />
    </UPageCard>
  </div>
</template>
