<script setup lang="ts">
// A time being dragged out over the first facility's column of the booking calendar, while the walkthrough asks for one to be picked: what to
// do, where it is to be done. It goes in the element around vue-cal, which is positioned, and lets every press through to the calendar.
defineProps<{
  touch?: boolean
}>()

const root = useTemplateRef<HTMLElement>('root')
const box = ref<{ left: number, top: number, width: number, height: number }>()

// Somewhere that can be picked and can be seen: below the line for now if it is on show, and below the facilities' names
function place() {
  const around = root.value?.parentElement
  const visible = around?.querySelector('.vuecal__scrollable-wrap')?.getBoundingClientRect()
  const column = around?.querySelector('.vuecal__schedule--cell')?.getBoundingClientRect()
  if (!around || !visible || !column) {
    box.value = undefined
    return
  }

  const origin = around.getBoundingClientRect()
  const headings = around.querySelector('.vuecal__headings')?.getBoundingClientRect()
  const now = around.querySelector('.vuecal__now-line')?.getBoundingClientRect()
  const top = Math.max(visible.top, headings?.bottom ?? 0) + 12
  const bottom = visible.bottom - 12
  const afterNow = now && now.bottom > top && now.bottom + 72 < bottom ? now.bottom + 8 : top
  const width = Math.min(column.width * 0.7, 160)

  box.value = {
    left: column.left - origin.left + (column.width - width) / 2,
    top: afterNow - origin.top,
    width,
    height: Math.max(Math.min(120, bottom - afterNow), 40),
  }
}

onMounted(() => setTimeout(place, 50))
useResizeObserver(() => root.value?.parentElement, place)
</script>

<template>
  <div
    ref="root"
    class="ghost absolute inset-0 z-[5] overflow-hidden"
    data-testid="booking-ghost"
  >
    <div
      v-if="box"
      class="absolute opacity-80"
      :style="{ left: `${box.left}px`, top: `${box.top}px`, width: `${box.width}px`, height: `${box.height}px` }"
    >
      <BookingHelpGesture :touch="touch" />
    </div>
  </div>
</template>

<style scoped>
/* driver.js lets every press through to whatever is inside what it highlights, which this is, unless told otherwise more strongly */
.ghost,
.ghost :deep(*) {
  pointer-events: none !important;
}
</style>
