<script setup lang="ts">
import { useGetOrgBookings, useGetOrgFacilitiesBookable, type FbsWebApiEndpointsOrgBookingsBookingResponse } from '~/api'
import { addDaysIn, startOfDayIn } from '~/lib/slots'
import { DAY_WIDTH, HOUR_WIDTH, dayStarts, layoutTimeline, positionOf } from '~/lib/timeline'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Timeline' })

type Booking = FbsWebApiEndpointsOrgBookingsBookingResponse

const HOURS = Array.from({ length: 24 }, (_, i) => i)

const router = useRouter()
const { slug, isAdmin, timeZone, path } = useTenant()
const { df, tf } = useTenantFormatter()

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

const now = useNow({ interval: 60_000 })
const days = computed(() => rangeQuery.value)

// The window starts at the beginning of today where the organization is, so the "now" marker is always in view
const windowStart = computed(() => startOfDayIn(now.value, timeZone.value))
const windowEnd = computed(() => addDaysIn(windowStart.value, days.value, timeZone.value))
const totalWidth = computed(() => days.value * DAY_WIDTH)
const dayColumns = computed(() => dayStarts(windowStart.value, days.value, timeZone.value))

const { data: facilities, isPending: facilitiesIsPending } = useGetOrgFacilitiesBookable({ path: computed(() => ({ slug: slug.value })) })
const { data: bookings, isPending: bookingsIsPending, error } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: windowStart.value, to: windowEnd.value, mine: false })),
})

const facilityItems = computed(() => facilityMenuItems(facilities.value))
const selectedFacilities = computed({
  get: () => facilityQuery.value,
  set: (value: string[]) => { facilityQuery.value = value },
})

const sections = computed(() => layoutTimeline<Booking>({
  bookings: bookings.value ?? [],
  facilities: facilities.value ?? [],
  from: windowStart.value,
  days: days.value,
  now: now.value,
  timeZone: timeZone.value,
  only: selectedFacilities.value,
  onlyBooked: onlyBooked.value,
}))

const upcomingCount = computed(() => sections.value.reduce((sum, section) => sum + section.rows.reduce((rows, row) => rows + row.blocks.length, 0), 0))
const hasFilters = computed(() => selectedFacilities.value.length > 0 || onlyBooked.value)
const isPending = computed(() => bookingsIsPending.value || facilitiesIsPending.value)
const noFacilities = computed(() => facilities.value !== undefined && facilities.value.length === 0 && !(bookings.value?.length))

const nowPosition = computed(() => positionOf(now.value, windowStart.value, timeZone.value))

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

function toggleFacility(id: string) {
  selectedFacilities.value = selectedFacilities.value.includes(id)
    ? selectedFacilities.value.filter(f => f !== id)
    : [...selectedFacilities.value, id]
}

const nameOfFacility = (id: string) => facilities.value?.find(f => f.id === id)?.name ?? 'A facility that is gone'

function describe(booking: Booking) {
  const { startDateTime: start, endDateTime: end } = booking
  const sameDay = startOfDayIn(start, timeZone.value).getTime() === startOfDayIn(end, timeZone.value).getTime()
  return sameDay
    ? `${df.value.format(start)}, ${tf.value.format(start)} – ${tf.value.format(end)}`
    : `${df.value.format(start)}, ${tf.value.format(start)} – ${df.value.format(end)}, ${tf.value.format(end)}`
}

const isUnderway = (booking: Booking) => booking.startDateTime <= now.value

const contact = (booking: Booking) => [booking.pocName, booking.pocPhone && `(${booking.pocPhone})`].filter(Boolean).join(' ')

function open(booking: Booking) {
  router.push(path('bookings', booking.id))
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
            :to="path('book')"
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
          v-for="id in selectedFacilities"
          :key="id"
          color="primary"
          variant="subtle"
          size="xs"
          trailing-icon="i-lucide-x"
          :label="nameOfFacility(id)"
          :aria-label="`Remove ${nameOfFacility(id)} from the filter`"
          @click="toggleFacility(id)"
        />
      </div>

      <UAlert
        v-if="error"
        title="Couldn't load the bookings"
        :description="getErrorReasons(error)[0] ?? 'Try fewer days, or try again.'"
        color="error"
        variant="subtle"
        icon="i-lucide-circle-alert"
      />

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
        <p v-if="noFacilities">
          {{ isAdmin ? 'There are no facilities yet.' : 'There are no facilities to book yet.' }}
        </p>
        <p v-else>
          No facilities to show.
        </p>
        <UButton
          v-if="hasFilters"
          label="Clear filters"
          color="neutral"
          variant="outline"
          @click="clearFilters"
        />
        <UButton
          v-else-if="noFacilities && isAdmin"
          :to="path('admin', 'facilities')"
          label="Add facilities"
          color="neutral"
          variant="outline"
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
            data-testid="now-marker"
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
              :key="row.facilityId"
              class="flex h-12 border-b border-default"
            >
              <button
                type="button"
                class="sticky left-0 z-10 w-40 shrink-0 cursor-pointer truncate border-r border-default bg-default px-3 text-left text-sm font-medium text-highlighted hover:bg-elevated"
                :title="selectedFacilities.includes(row.facilityId) ? 'Show all facilities' : `Only show ${row.name}`"
                @click="selectedFacilities = selectedFacilities.length === 1 && selectedFacilities[0] === row.facilityId ? [] : [row.facilityId]"
              >
                {{ row.name }}
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
                    :aria-label="`${row.name}: ${block.booking.conduct}, ${describe(block.booking)}`"
                    @click="open(block.booking)"
                  >
                    <span class="block truncate">{{ block.booking.conduct }}</span>
                  </button>

                  <template #content>
                    <div class="space-y-0.5 p-1 text-xs">
                      <p class="font-semibold">
                        {{ block.booking.conduct }}
                      </p>
                      <p>{{ row.name }}</p>
                      <p>{{ describe(block.booking) }}</p>
                      <p v-if="block.booking.pocName">
                        {{ contact(block.booking) }}
                      </p>
                      <p class="text-muted">
                        Booked by {{ block.booking.bookedBy.displayName }}
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
