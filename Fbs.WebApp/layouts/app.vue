<script setup lang="ts">
const { error } = useMe()

const router = useRouter()

whenever(error, async () => {
  await router.push('/auth/login')
})
</script>

<template>
  <UDashboardGroup unit="rem">
    <AppSidebar />

    <slot />
  </UDashboardGroup>
</template>
