<script setup lang="ts">
const emit = defineEmits(['click', 'mouseover'])

const props = defineProps<{
  state: 'available' | 'selected' | 'hovered'
  timeSlot: string
}>()

const { tf } = useFormatter()

const styles = computed(() => {
  switch (props.state) {
    case 'available':
      return 'bg-green-100 border-green-400 cursor-pointer'
    case 'selected':
      return 'bg-blue-100 border-blue-400 cursor-pointer'
    case 'hovered':
      return 'bg-blue-50 border-blue-400'
    default:
      return 'bg-gray-100 border-gray-400'
  }
})

const slotDate = computed(() => new Date(props.timeSlot))
</script>

<template>
  <UCard
    :ui="{ body: `p-2 sm:p-2 text-center text-sm border-1 rounded-sm ${styles}` }"
    @click="emit('click')"
    @mouseover="emit('mouseover')"
  >
    {{ tf.format(slotDate) }}
  </UCard>
</template>
