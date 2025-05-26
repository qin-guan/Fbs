<script setup lang="ts">
const { visible } = useSidebar()
</script>

<template>
  <div>
    <Drawer
      v-model:visible="visible"
      class="flex lg:hidden"
      :show-close-icon="false"
    >
      <template #container>
        <AppSidebarMenu />
      </template>
    </Drawer>

    <div class="hidden lg:flex h-full">
      <AppSidebarMenu />
    </div>
  </div>
</template>
