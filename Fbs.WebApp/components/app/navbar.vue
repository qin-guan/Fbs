<script setup lang="ts">
const { visible: sidebarVisible } = useSidebar()
</script>

<template>
  <div class="flex items-center px-1 h-12 border-b-1 border-gray-300 gap-3">
    <div
      class="flex lg:hidden"
    >
      <Button
        variant="text"
        @click="sidebarVisible = true"
      >
        <template #icon>
          <Icon
            name="i-lucide-menu"
            class="text-black"
          />
        </template>
      </Button>
    </div>

    <div class="lg:ml-3 w-full">
      <slot name="content" />
    </div>
  </div>
</template>
