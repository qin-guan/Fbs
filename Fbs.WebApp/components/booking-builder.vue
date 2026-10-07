<script setup lang="ts">
import { getLocalTimeZone, today, type DateValue } from '@internationalized/date'
import { useGetBooking, useGetFacility } from '~/api'
import type { NewBookingSlot } from '~/composables/booking-slots'

// Pick several facilities and days at once, and add the resulting slots to the booking list.
const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  add: [slots: NewBookingSlot[]]
}>()

const { data: facilities } = useGetFacility()
const { data: bookings } = useGetBooking()
const basket = useBookingBasket()
const { tf } = useFormatter()

const selectedFacilities = ref<string[]>([])
// shallowRef keeps the DateValue class types intact for UCalendar
const selectedDays = shallowRef<DateValue[]>([])
const allDay = ref(true)
const from = ref('08:00')
const to = ref('12:00')
const split = ref(true)

const minDate = today(getLocalTimeZone())

const facilityItems = computed(() => {
  const groups = new Map<string, string[]>()
  for (const facility of facilities.value ?? []) {
    if (!facility.name) continue
    const group = facility.group ?? 'Other'
    groups.set(group, [...(groups.get(group) ?? []), facility.name])
  }
  return [...groups].map(([group, names]) => [
    { type: 'label' as const, label: group },
    ...names.map(name => ({ label: name, value: name })),
  ])
})

const times = Array.from({ length: 49 }, (_, i) => {
  const hours = String(Math.floor(i / 2)).padStart(2, '0')
  const minutes = i % 2 ? '30' : '00'
  const value = `${hours}:${minutes}`
  const date = new Date(2000, 0, 1, Math.floor(i / 2), i % 2 ? 30 : 0)
  return { value, label: i === 48 ? 'Midnight (end of day)' : tf.format(date) }
})
const fromTimes = times.slice(0, 48)
const toTimes = times.slice(1)

const days = computed(() => selectedDays.value.map(d => d.toDate(getLocalTimeZone())))
const hasConsecutiveDays = computed(() => consecutiveRuns(days.value).some(run => run.length > 1))
const timeError = computed(() => !allDay.value && to.value <= from.value ? 'End time must be after the start time.' : undefined)

const plan = computed(() => planSlots({
  facilities: selectedFacilities.value,
  days: days.value,
  allDay: allDay.value,
  from: from.value,
  to: to.value,
  split: split.value || !hasConsecutiveDays.value,
}))

const preview = computed(() => timeError.value ? [] : plan.value.slots)
const clashes = computed(() => findClashes([...basket.slots.value, ...preview.value], bookings.value).slice(basket.slots.value.length))
const clashCount = computed(() => clashes.value.filter(Boolean).length)
const overLimit = computed(() => basket.slots.value.length + preview.value.length > MAX_BATCH_SLOTS)

function add() {
  emit('add', preview.value)
  open.value = false
  selectedDays.value = []
}
</script>

<template>
  <USlideover
    v-model:open="open"
    title="Book multiple facilities or days"
    description="One conduct and one point of contact for the lot. You fill those in next."
    :ui="{ content: 'max-w-lg', body: 'space-y-6' }"
  >
    <template #body>
      <UFormField
        label="Facilities"
        name="builder-facilities"
      >
        <USelectMenu
          v-model="selectedFacilities"
          :items="facilityItems"
          value-key="value"
          multiple
          placeholder="Choose facilities"
          icon="i-lucide-building-2"
          class="w-full"
        />
      </UFormField>

      <UFormField
        label="Days"
        :hint="selectedDays.length ? `${selectedDays.length} selected` : undefined"
      >
        <div class="rounded-lg border border-default p-2">
          <UCalendar
            v-model="selectedDays"
            multiple
            :min-value="minDate"
            class="w-full"
          />
        </div>
      </UFormField>

      <div class="space-y-4">
        <USwitch
          v-model="allDay"
          label="All day"
          description="Midnight to midnight"
        />

        <div
          v-if="!allDay"
          class="grid grid-cols-2 gap-3"
        >
          <UFormField
            label="From"
            :error="timeError ? true : undefined"
          >
            <USelect
              v-model="from"
              :items="fromTimes"
              class="w-full"
            />
          </UFormField>
          <UFormField
            label="To"
            :error="timeError"
          >
            <USelect
              v-model="to"
              :items="toTimes"
              class="w-full"
            />
          </UFormField>
        </div>

        <USwitch
          v-if="hasConsecutiveDays"
          v-model="split"
          label="Split multi-day bookings"
          :description="split
            ? 'Each day is booked separately.'
            : allDay
              ? 'Consecutive days are booked as one continuous booking.'
              : 'Consecutive days are booked as one continuous booking, from the start time on the first day to the end time on the last.'"
        />
      </div>

      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-sm font-semibold text-highlighted">
            {{ preview.length }} {{ preview.length === 1 ? 'booking' : 'bookings' }}
          </h3>
          <span
            v-if="clashCount"
            class="text-xs text-error"
          >{{ clashCount === 1 ? '1 slot clashes' : `${clashCount} slots clash` }}</span>
        </div>

        <p
          v-if="!preview.length"
          class="text-sm text-muted"
        >
          Pick a facility and a day to see what would be booked.
        </p>

        <BookingSlotList
          v-else
          :slots="preview"
          :clashes="clashes"
          :offset="basket.slots.value.length"
          class="max-h-72 overflow-y-auto rounded-lg border border-default px-2"
        />

        <p
          v-if="plan.skipped"
          class="text-xs text-muted"
        >
          {{ plan.skipped }} {{ plan.skipped === 1 ? 'slot is' : 'slots are' }} in the past and left out.
        </p>

        <UAlert
          v-if="overLimit"
          color="warning"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          :title="`You can book up to ${MAX_BATCH_SLOTS} slots at a time.`"
          :description="basket.slots.value.length ? `Your list already has ${basket.slots.value.length}.` : undefined"
        />
      </div>
    </template>

    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton
          label="Cancel"
          color="neutral"
          variant="outline"
          @click="open = false"
        />
        <UButton
          :label="preview.length ? `Add ${preview.length} to list` : 'Add to list'"
          icon="i-lucide-list-plus"
          :disabled="!preview.length || overLimit"
          @click="add"
        />
      </div>
    </template>
  </USlideover>
</template>
