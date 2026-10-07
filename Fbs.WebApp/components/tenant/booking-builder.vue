<script setup lang="ts">
import { CalendarDate, today, type DateValue } from '@internationalized/date'
import { useGetOrgBookings, useGetOrgFacilitiesBookable } from '~/api'
import { consecutiveRuns, findClashes, MAX_BATCH_SLOTS, planSlots, startOfDayIn, type Slot } from '~/lib/slots'

// Pick several facilities and days at once, and add the resulting slots to the booking list.
const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  add: [slots: Slot[]]
}>()

const { slug, org, timeZone } = useTenant()
const { data: facilities } = useGetOrgFacilitiesBookable({ path: computed(() => ({ slug: slug.value })) })
const basket = useTenantBasket()
const now = useNow({ interval: 60_000 })

const selectedFacilities = ref<string[]>([])
// shallowRef keeps the DateValue class types intact for UCalendar
const selectedDays = shallowRef<DateValue[]>([])
const allDay = ref(true)
const from = ref('08:00')
const to = ref('12:00')
const split = ref(true)

const minDate = computed(() => today(timeZone.value))

const zone = computed(() => ({ timeZone: timeZone.value, slotMinutes: org.value?.slotMinutes ?? 30 }))
const days = computed(() => selectedDays.value.map(d => new CalendarDate(d.year, d.month, d.day)))
const hasConsecutiveDays = computed(() => consecutiveRuns(days.value).some(run => run.length > 1))
const timeError = computed(() => !allDay.value && to.value <= from.value ? 'End time must be after the start time.' : undefined)

// Clashes are looked for among the bookings of the days chosen, from the first to the last
const span = computed(() => {
  if (!days.value.length) {
    return undefined
  }

  const sorted = [...days.value].sort((a, b) => a.compare(b))
  return { from: startOfDayIn(sorted[0]!.toDate(timeZone.value), timeZone.value), to: startOfDayIn(sorted.at(-1)!.add({ days: 1 }).toDate(timeZone.value), timeZone.value) }
})
const spanIsShortEnough = computed(() => !!span.value && span.value.to.getTime() - span.value.from.getTime() <= 93 * 86_400_000)
const { data: bookings } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: span.value?.from, to: span.value?.to, mine: false })),
}, { query: { enabled: spanIsShortEnough } })

// A time on the wall, such as 09:00, as it is written: it is not a moment, so it is formatted as one in UTC, which changes nothing
const timeLabel = (value: string) => new Intl.DateTimeFormat('en-SG', { timeStyle: 'short', timeZone: 'UTC' }).format(new Date(`2000-01-01T${value}:00Z`))

const times = computed(() => {
  const step = zone.value.slotMinutes
  const count = Math.floor(24 * 60 / step)
  return Array.from({ length: count + 1 }, (_, i) => {
    const minutes = i * step
    const value = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
    return { value, label: i === count ? 'Midnight (end of day)' : timeLabel(value) }
  })
})
const fromTimes = computed(() => times.value.slice(0, -1))
const toTimes = computed(() => times.value.slice(1))

const plan = computed(() => planSlots({
  facilityIds: selectedFacilities.value,
  days: days.value,
  allDay: allDay.value,
  from: from.value,
  to: to.value,
  split: split.value || !hasConsecutiveDays.value,
}, now.value, zone.value))

const preview = computed(() => timeError.value ? [] : plan.value.slots)
const clashes = computed(() => findClashes([...basket.slots.value, ...preview.value], bookings.value, now.value).slice(basket.slots.value.length))
const clashCount = computed(() => clashes.value.filter(Boolean).length)
const overLimit = computed(() => basket.slots.value.length + preview.value.length > MAX_BATCH_SLOTS)

function add() {
  emit('add', preview.value)
  open.value = false
  selectedDays.value = []
}

function close() {
  open.value = false
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
          :items="facilityMenuItems(facilities)"
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

        <TenantSlotList
          v-else
          :slots="preview"
          :facilities="facilities?.map(f => ({ id: f.id, name: f.name })) ?? []"
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
          @click="close"
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
