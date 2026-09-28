<script setup lang="ts">
import { useGetAuthMe } from '~/api'

const { error } = useGetAuthMe()

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
