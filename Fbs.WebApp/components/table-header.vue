<script setup lang="ts">
import type { Column, Header } from '@tanstack/vue-table'

// Column header for UTable with click-to-sort (shift+click for multi-sort) and drag-to-resize.
type SortableResizableHeader = Pick<Header<unknown, unknown>, 'getResizeHandler'> & {
  column: Pick<Column<unknown, unknown>, 'getIsSorted' | 'getCanSort' | 'getToggleSortingHandler' | 'getCanResize' | 'getIsResizing' | 'resetSize'>
}

const props = defineProps<{
  header: SortableResizableHeader
  label: string
}>()

const sorted = computed(() => props.header.column.getIsSorted())
const sortIcon = computed(() => {
  if (sorted.value === 'asc') return 'i-lucide-arrow-up-narrow-wide'
  if (sorted.value === 'desc') return 'i-lucide-arrow-down-wide-narrow'
  return 'i-lucide-arrow-up-down'
})

function onResizeStart(e: MouseEvent | TouchEvent) {
  props.header.getResizeHandler()(e)
}
</script>

<template>
  <div class="flex items-center">
    <UButton
      v-if="header.column.getCanSort()"
      color="neutral"
      variant="ghost"
      :label="label"
      :trailing-icon="sortIcon"
      :aria-label="`Sort by ${label}`"
      class="-mx-2.5 -my-1.5 font-semibold text-highlighted"
      :ui="{ trailingIcon: sorted ? 'text-primary' : 'text-dimmed' }"
      @click="header.column.getToggleSortingHandler()?.($event)"
    />
    <span v-else>{{ label }}</span>

    <div
      v-if="header.column.getCanResize()"
      role="separator"
      aria-orientation="vertical"
      :aria-label="`Resize ${label} column`"
      class="absolute inset-y-0 -end-1 z-[2] w-2 cursor-col-resize touch-none select-none after:absolute after:inset-y-2.5 after:start-1/2 after:w-px after:bg-(--ui-border) hover:after:bg-primary"
      :class="{ 'after:bg-primary': header.column.getIsResizing() }"
      @mousedown="onResizeStart"
      @touchstart="onResizeStart"
      @dblclick="header.column.resetSize()"
    />
  </div>
</template>
