<script setup lang="ts">
const { tf } = useFormatter()

const emit = defineEmits(['click', 'mouseover'])
const slots = defineSlots<{
  tooltip(): unknown
}>()

const props = defineProps<{
  state: 'available' | 'unavailable' | 'pastBookingDate' | 'selected' | 'hovered'
  timeSlot: string
}>()

const slotDate = computed(() => new Date(props.timeSlot))

const styles = computed(() => {
  switch (props.state) {
    case 'available':
      return 'bg-green-100 border-green-400 cursor-pointer'
    case 'pastBookingDate':
      return 'bg-gray-100 border-gray-400'
    case 'unavailable':
      return 'bg-gray-100 border-gray-400'
    case 'selected':
      return 'bg-blue-100 border-blue-400 cursor-pointer'
    case 'hovered':
      return 'bg-blue-50 border-blue-400'
    default:
      return 'bg-red-100 border-red-400'
  }
})
</script>

<template>
  <UTooltip
    v-if="slots.tooltip"
    :ui="{ content: 'select-text text-lg h-auto' }"
  >
    <template #content>
      <slot name="tooltip" />
    </template>

    <UCard
      :ui="{ body: `p-2 sm:p-2 text-center text-sm border-1 rounded-sm cursor-not-allowed ${styles}` }"
      @click="emit('click')"
      @mouseover="emit('mouseover')"
    >
      {{ tf.format(slotDate) }}
    </UCard>
  </UTooltip>

  <UTooltip
    v-else-if="state === 'pastBookingDate'"
  >
    <template #content>
      Past slot availability
    </template>

    <UCard
      :ui="{ body: `p-2 sm:p-2 text-center text-sm border-1 rounded-sm cursor-not-allowed ${styles}` }"
      @click="emit('click')"
      @mouseover="emit('mouseover')"
    >
      {{ tf.format(slotDate) }}
    </UCard>
  </UTooltip>

  <UCard
    v-else
    :ui="{ body: `p-2 sm:p-2 text-center text-sm border-1 rounded-sm ${styles}` }"
    @click="emit('click')"
    @mouseover="emit('mouseover')"
  >
    {{ tf.format(slotDate) }}
  </UCard>
</template>
