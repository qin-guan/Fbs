<script setup lang="ts">
import type { BookingHint } from '~/composables/booking-help'

// Help offered on the booking page to somebody who looks stuck picking a time (see useBookingStruggle): how it is done, a way to be shown on
// the calendar itself, and a way to choose the times from a list instead, for whoever can't drag.
const props = defineProps<{
  hint: BookingHint
  touch: boolean
}>()

const emit = defineEmits<{
  tour: []
  list: []
  close: []
}>()

const titles: Record<BookingHint, string> = {
  tap: 'Picking a time?',
  hold: 'Almost there',
  drag: 'Nothing was picked',
  idle: 'Need a hand?',
}
</script>

<template>
  <section
    role="status"
    aria-live="polite"
    aria-labelledby="booking-hint-title"
    data-testid="booking-hint"
    class="pointer-events-auto flex w-full max-w-md gap-3 rounded-lg border border-default bg-default p-3 shadow-lg"
  >
    <div class="relative hidden h-24 w-16 shrink-0 overflow-hidden rounded-md border border-default bg-elevated/50 min-[400px]:block">
      <div class="absolute inset-x-3 top-3 bottom-5">
        <BookingHelpGesture :touch="props.touch" />
      </div>
    </div>

    <div class="min-w-0 flex-1">
      <div class="flex items-start justify-between gap-2">
        <h2
          id="booking-hint-title"
          class="text-sm font-semibold text-highlighted"
        >
          {{ titles[props.hint] }}
        </h2>
        <UButton
          icon="i-lucide-x"
          color="neutral"
          variant="ghost"
          size="xs"
          aria-label="Close"
          class="-me-1 -mt-1"
          @click="emit('close')"
        />
      </div>
      <p class="text-sm text-muted">
        {{ bookingHintText(props.hint, props.touch) }}
      </p>
      <div class="mt-2 flex flex-wrap gap-1.5">
        <UButton
          label="Show me"
          icon="i-lucide-play"
          size="xs"
          @click="emit('tour')"
        />
        <UButton
          label="Pick times from a list"
          icon="i-lucide-list"
          color="neutral"
          variant="outline"
          size="xs"
          @click="emit('list')"
        />
      </div>
    </div>
  </section>
</template>
