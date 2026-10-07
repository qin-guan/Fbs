<script setup lang="ts">
// The pages for admins. What they change can't be changed by anybody else, and the API says so, but there is no point in showing a
// member a page of errors: and the links to them are not shown to a member either.
const { isAdmin, path } = useTenant()
</script>

<template>
  <slot v-if="isAdmin" />
  <div
    v-else
    class="flex flex-1 items-center justify-center p-6"
  >
    <UPageCard
      class="max-w-md"
      variant="subtle"
      icon="i-lucide-shield-alert"
      title="Only admins can do this"
      description="An admin here has to make the change, or make you an admin first."
    >
      <UButton
        :to="path()"
        label="Back to the bookings"
        color="neutral"
        variant="subtle"
      />
    </UPageCard>
  </div>
</template>
