<script setup lang="ts">
import { useGetBooking, useGetFacility, type FbsWebApiDtosBookingWithUser } from '~/api'
import { addDays, overlaps, startOfDay } from '~/composables/booking-slots'

definePageMeta({
  layout: 'app',
})

type Booking = FbsWebApiDtosBookingWithUser

interface Block {
  booking: Booking
  left: number
  width: number
}

interface Row {
  facility: string
  blocks: Block[]
}

const HOUR_WIDTH = 28
const DAY_WIDTH = HOUR_WIDTH * 24
const HOURS = Array.from({ length: 24 }, (_, i) => i)

const router = useRouter()
const { df, tf } = useFormatter()

const { data: facilities, isPending: facilitiesIsPending } = useGetFacility()
const { data: bookings, isPending: bookingsIsPending } = useGetBooking()

const now = useNow({ interval: 60_000 })

const rangeOptions = [
  { label: '3 days', value: 3 },
  { label: '7 days', value: 7 },
  { label: '14 days', value: 14 },
  { label: '30 days', value: 30 },
]

// Filters live in the URL so a filtered timeline can be bookmarked or shared
const facilityQuery = useRouteQuery<string | string[] | null | undefined, string[]>('facility', [], {
  transform: {
    get: value => (Array.isArray(value) ? value : value ? [value] : []),
    set: value => value,
  },
})
const rangeQuery = useRouteQuery<string | string[] | null | undefined, number>('days', '7', {
  transform: {
    get: (value) => {
      const days = Number(Array.isArray(value) ? value[0] : value)
      return rangeOptions.some(o => o.value === days) ? days : 7
    },
    set: value => String(value),
  },
})
const onlyBooked = ref(false)

const groupByFacility = computed(() => {
  const groups = new Map<string, string>()
  for (const facility of facilities.value ?? []) {
    if (facility.name) groups.set(facility.name, facility.group ?? 'Other')
  }
  return groups
})

const facilityItems = computed(() => {
  const groups = new Map<string, string[]>()
  for (const [name, group] of groupByFacility.value) {
    groups.set(group, [...(groups.get(group) ?? []), name])
  }
  return [...groups].map(([group, names]) => [
    { type: 'label' as const, label: group },
    ...names.map(name => ({ label: name, value: name })),
  ])
})

const selectedFacilities = computed({
  get: () => facilityQuery.value,
  set: (value: string[]) => { facilityQuery.value = value },
})

const days = computed(() => rangeQuery.value)

// The window starts at the beginning of today, so the "now" marker is always in view
const windowStart = computed(() => startOfDay(now.value))
const windowEnd = computed(() => addDays(windowStart.value, days.value))
const totalWidth = computed(() => days.value * DAY_WIDTH)

const dayColumns = computed(() => Array.from({ length: days.value }, (_, i) => addDays(windowStart.value, i)))

function positionOf(date: Date) {
  return ((date.getTime() - windowStart.value.getTime()) / (60 * 60 * 1000)) * HOUR_WIDTH
}

// Bookings that haven't finished yet, by facility
const upcoming = computed(() => {
  const window = { start: windowStart.value, end: windowEnd.value }
  const byFacility = new Map<string, Block[]>()

  for (const booking of bookings.value ?? []) {
    const { facilityName, startDateTime, endDateTime } = booking
    if (!facilityName || !startDateTime || !endDateTime || endDateTime <= now.value) continue
    if (!overlaps({ start: startDateTime, end: endDateTime }, window)) continue

    const left = Math.max(positionOf(startDateTime), 0)
    const right = Math.min(positionOf(endDateTime), totalWidth.value)
    byFacility.set(facilityName, [...(byFacility.get(facilityName) ?? []), { booking, left, width: right - left }])
  }

  for (const blocks of byFacility.values()) {
    blocks.sort((a, b) => a.left - b.left)
  }
  return byFacility
})

const rows = computed<Row[]>(() => {
  // Facilities that no longer exist can still have bookings
  const names = new Set([...groupByFacility.value.keys(), ...upcoming.value.keys()])
  const selected = selectedFacilities.value

  return [...names]
    .filter(name => !selected.length || selected.includes(name))
    .map(facility => ({ facility, blocks: upcoming.value.get(facility) ?? [] }))
    .filter(row => !onlyBooked.value || row.blocks.length > 0)
})

const sections = computed(() => {
  const groups = new Map<string, Row[]>()
  for (const row of rows.value) {
    const group = groupByFacility.value.get(row.facility) ?? 'Other'
    groups.set(group, [...(groups.get(group) ?? []), row])
  }
  return [...groups].map(([group, groupRows]) => ({ group, rows: groupRows }))
})

const upcomingCount = computed(() => rows.value.reduce((sum, row) => sum + row.blocks.length, 0))
const hasFilters = computed(() => selectedFacilities.value.length > 0 || onlyBooked.value)
const isPending = computed(() => bookingsIsPending.value || facilitiesIsPending.value)

const nowPosition = computed(() => positionOf(now.value))

// Timeline is scrolled so that "now" is at the left, once the data has loaded
const scroller = useTemplateRef('scroller')
function scrollToNow() {
  scroller.value?.scrollTo({ left: Math.max(nowPosition.value - HOUR_WIDTH * 2, 0) })
}
watch([isPending, days], async () => {
  if (isPending.value) return
  await nextTick()
  scrollToNow()
}, { immediate: true, flush: 'post' })

function clearFilters() {
  selectedFacilities.value = []
  onlyBooked.value = false
}

function toggleFacility(name: string) {
  selectedFacilities.value = selectedFacilities.value.includes(name)
    ? selectedFacilities.value.filter(f => f !== name)
    : [...selectedFacilities.value, name]
}

function describe(booking: Booking) {
  if (!booking.startDateTime || !booking.endDateTime) return ''
  const { startDateTime: start, endDateTime: end } = booking
  const sameDay = startOfDay(start).getTime() === startOfDay(end).getTime()
  return sameDay
    ? `${df.format(start)}, ${tf.format(start)} – ${tf.format(end)}`
    : `${df.format(start)}, ${tf.format(start)} – ${df.format(end)}, ${tf.format(end)}`
}

function isUnderway(booking: Booking) {
  return !!booking.startDateTime && booking.startDateTime <= now.value
}

function open(booking: Booking) {
  if (booking.id) router.push(`/booking/${booking.id}`)
}
</script>

<template>
  <UDashboardPanel id="timeline">
    <template #header>
      <AppNavbar title="Timeline">
        <template #trailing>
          <UBadge
            v-if="!isPending"
            :label="upcomingCount"
            variant="subtle"
          />
        </template>

        <template #right>
          <UButton
            to="/booking/new"
            icon="i-lucide-plus"
            label="New"
          />
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="flex flex-wrap items-center gap-2">
        <USelectMenu
          v-model="selectedFacilities"
          :items="facilityItems"
          value-key="value"
          multiple
          icon="i-lucide-building-2"
          placeholder="All facilities"
          aria-label="Filter by facility"
          class="min-w-0 flex-1 sm:w-72 sm:flex-none"
        />

        <USelect
          v-model="rangeQuery"
          :items="rangeOptions"
          icon="i-lucide-calendar-range"
          aria-label="Days to show"
          class="w-32"
        />

        <USwitch
          v-model="onlyBooked"
          label="Only with bookings"
          class="mx-1"
        />

        <UButton
          type="button"
          color="neutral"
          variant="ghost"
          icon="i-lucide-funnel-x"
          aria-label="Clear filters"
          :disabled="!hasFilters"
          @click="clearFilters"
        >
          <span class="hidden sm:inline">Clear</span>
        </UButton>

        <UButton
          type="button"
          color="neutral"
          variant="outline"
          icon="i-lucide-locate-fixed"
          class="sm:ml-auto"
          @click="scrollToNow"
        >
          <span class="hidden sm:inline">Now</span>
        </UButton>
      </div>

      <div
        v-if="selectedFacilities.length"
        class="flex flex-wrap items-center gap-1.5"
      >
        <UButton
          v-for="name in selectedFacilities"
          :key="name"
          color="primary"
          variant="subtle"
          size="xs"
          trailing-icon="i-lucide-x"
          :label="name"
          :aria-label="`Remove ${name} from the filter`"
          @click="toggleFacility(name)"
        />
      </div>

      <div
        v-if="isPending"
        class="flex flex-1 items-center justify-center text-muted"
      >
        <UIcon
          name="i-lucide-loader-circle"
          class="size-6 animate-spin"
        />
      </div>

      <div
        v-else-if="!sections.length"
        class="flex flex-1 flex-col items-center justify-center gap-2 text-center text-muted"
      >
        <UIcon
          name="i-lucide-calendar-x"
          class="size-8"
        />
        <p>No facilities to show.</p>
        <UButton
          v-if="hasFilters"
          label="Clear filters"
          color="neutral"
          variant="outline"
          @click="clearFilters"
        />
      </div>

      <div
        v-else
        ref="scroller"
        class="flex-1 min-h-0 overflow-auto rounded-lg border border-default"
      >
        <div
          class="relative"
          :style="{ width: `calc(10rem + ${totalWidth}px)` }"
        >
          <!-- Days and hours -->
          <div class="sticky top-0 z-30 flex border-b border-default bg-default">
            <div class="sticky left-0 z-40 w-40 shrink-0 border-r border-default bg-default" />
            <div
              v-for="day in dayColumns"
              :key="day.getTime()"
              class="shrink-0 border-r border-default"
              :style="{ width: `${DAY_WIDTH}px` }"
            >
              <div class="sticky left-40 w-max px-2 py-1 text-xs font-medium text-highlighted">
                {{ df.format(day) }}
              </div>
              <div class="flex">
                <span
                  v-for="hour in HOURS"
                  :key="hour"
                  class="shrink-0 border-l border-default/60 pl-1 text-[10px] text-dimmed"
                  :style="{ width: `${HOUR_WIDTH}px` }"
                >
                  <template v-if="hour % 3 === 0">{{ String(hour).padStart(2, '0') }}</template>
                </span>
              </div>
            </div>
          </div>

          <!-- Now marker -->
          <div
            class="pointer-events-none absolute bottom-0 top-0 z-[5] w-0.5 bg-red-500"
            :style="{ left: `calc(10rem + ${nowPosition}px)` }"
          />

          <template
            v-for="section in sections"
            :key="section.group"
          >
            <div class="sticky left-0 z-10 flex w-full border-b border-default bg-elevated">
              <span class="sticky left-0 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-muted">
                {{ section.group }}
              </span>
            </div>

            <div
              v-for="row in section.rows"
              :key="row.facility"
              class="flex h-12 border-b border-default"
            >
              <button
                type="button"
                class="sticky left-0 z-10 w-40 shrink-0 cursor-pointer truncate border-r border-default bg-default px-3 text-left text-sm font-medium text-highlighted hover:bg-elevated"
                :title="selectedFacilities.includes(row.facility) ? 'Show all facilities' : `Only show ${row.facility}`"
                @click="selectedFacilities = selectedFacilities.length === 1 && selectedFacilities[0] === row.facility ? [] : [row.facility]"
              >
                {{ row.facility }}
              </button>

              <div
                class="relative shrink-0"
                :style="{
                  'width': `${totalWidth}px`,
                  'backgroundImage': 'repeating-linear-gradient(to right, transparent 0, transparent calc(var(--day) - 1px), var(--ui-border) calc(var(--day) - 1px), var(--ui-border) var(--day))',
                  '--day': `${DAY_WIDTH}px`,
                }"
              >
                <!-- Tooltips are a fixed height by default, which cuts off the background behind multiple lines -->
                <UTooltip
                  v-for="block in row.blocks"
                  :key="block.booking.id"
                  :delay-duration="100"
                  :ui="{ content: 'h-auto' }"
                >
                  <button
                    type="button"
                    class="absolute inset-y-1.5 cursor-pointer overflow-hidden rounded-md px-2 text-left text-xs font-medium ring-1 ring-inset transition-colors"
                    :class="isUnderway(block.booking)
                      ? 'bg-primary text-inverted ring-primary hover:bg-primary/90'
                      : 'bg-primary/15 text-highlighted ring-primary/40 hover:bg-primary/25'"
                    :style="{ left: `${block.left}px`, width: `${Math.max(block.width, 4)}px` }"
                    :aria-label="`${row.facility}: ${block.booking.conduct}, ${describe(block.booking)}`"
                    @click="open(block.booking)"
                  >
                    <span class="block truncate">{{ block.booking.conduct }}</span>
                  </button>

                  <template #content>
                    <div class="space-y-0.5 p-1 text-xs">
                      <p class="font-semibold">
                        {{ block.booking.conduct }}
                      </p>
                      <p>{{ row.facility }}</p>
                      <p>{{ describe(block.booking) }}</p>
                      <p v-if="block.booking.pocName">
                        {{ block.booking.pocName }} ({{ block.booking.pocPhone }})
                      </p>
                    </div>
                  </template>
                </UTooltip>
              </div>
            </div>
          </template>
        </div>
      </div>
    </template>
  </UDashboardPanel>
</template>
