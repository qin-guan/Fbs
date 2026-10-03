<script setup lang="ts">
import { describeSlot, type SlotClash } from '~/lib/slots'

const props = withDefaults(defineProps<{
  slots: Array<{ id?: string, facilityId: string, start: Date, end: Date }>
  /** The facilities, to say which is which */
  facilities: Array<{ id: string, name: string }>
  clashes?: Array<SlotClash | undefined>
  /** Errors from the API, by slot index */
  errors?: Record<number, string>
  /** Number of slots listed before these, so clash references line up */
  offset?: number
  removable?: boolean
}>(), {
  clashes: () => [],
  errors: () => ({}),
  offset: 0,
  removable: false,
})

const emit = defineEmits<{
  remove: [index: number]
}>()

const { df, tf, timeZone } = useTenantFormatter()

const names = computed(() => new Map(props.facilities.map(f => [f.id, f.name])))
const nameOf = (slot: { facilityId: string }) => names.value.get(slot.facilityId) ?? 'A facility that is gone'
const describe = (slot: { start: Date, end: Date }) => describeSlot(slot, df.value, tf.value, timeZone.value)

// Prefer what we know here, which names who holds the slot, over the API's message
function problem(index: number) {
  const clash = props.clashes[index]
  if (!clash) {
    return props.errors[index]
  }

  if (clash.kind === 'past') {
    return 'This time has already passed'
  }

  if (clash.kind === 'existing') {
    const who = [clash.booking.bookedBy?.displayName, clash.booking.conduct].filter(Boolean).join(' / ')
    return `Clashes with ${who || 'an existing booking'}`
  }

  return clash.index < props.offset ? 'Overlaps a slot already in your list' : `Overlaps slot ${clash.index - props.offset + 1}`
}
</script>

<template>
  <ol class="divide-y divide-default">
    <li
      v-for="(slot, index) in slots"
      :key="slot.id ?? index"
      class="flex items-center gap-3 py-2"
      :data-clash="!!problem(index) || undefined"
    >
      <span class="w-5 shrink-0 text-right text-xs tabular-nums text-dimmed">{{ index + 1 }}</span>

      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-medium text-highlighted">
          {{ nameOf(slot) }}
        </p>
        <p class="truncate text-xs text-muted">
          {{ describe(slot).date }} · {{ describe(slot).time }}
        </p>
        <p
          v-if="problem(index)"
          class="mt-0.5 flex items-center gap-1 text-xs text-error"
        >
          <UIcon
            name="i-lucide-circle-alert"
            class="size-3.5 shrink-0"
          />
          <span class="truncate">{{ problem(index) }}</span>
        </p>
      </div>

      <UButton
        v-if="removable"
        color="neutral"
        variant="ghost"
        size="sm"
        icon="i-lucide-x"
        :aria-label="`Remove ${nameOf(slot)}, ${describe(slot).date}`"
        @click="emit('remove', index)"
      />
    </li>
  </ol>
</template>
