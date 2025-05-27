<script setup lang="ts">
const { error } = useMe()

const router = useRouter()

whenever(error, async () => {
  await router.push('/auth/login')
})
</script>

<template>
  <div class="h-full flex card">
    <AppSidebar />

    <div class="flex-1">
      <slot />
    </div>
  </div>
</template>
