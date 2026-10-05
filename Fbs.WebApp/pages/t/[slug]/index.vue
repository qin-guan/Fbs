<script setup lang="ts">
import { h } from 'vue'
import type { TableColumn } from '@nuxt/ui'
import type { Column, HeaderContext } from '@tanstack/vue-table'
import { useGetOrgBookings, useGetOrgFacilitiesBookable, type FbsWebApiEndpointsOrgBookingsBookingResponse } from '~/api'
import TableHeader from '~/components/table-header.vue'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Bookings' })

type Booking = FbsWebApiEndpointsOrgBookingsBookingResponse

const router = useRouter()
const { slug, org, isAdmin, timeZone, path } = useTenant()
const { df, tf } = useTenantFormatter()

// What is asked for is a window of time, as an organization can have thousands of bookings and nobody looks at all of them
const ranges = [
  { label: 'Next 7 days', value: 'week', from: 0, to: 7 },
  { label: 'Next 31 days', value: 'month', from: 0, to: 31 },
  { label: 'Next 3 months', value: 'quarter', from: 0, to: 93 },
  { label: 'Last 31 days', value: 'past-month', from: -31, to: 1 },
  { label: 'Last 3 months', value: 'past-quarter', from: -92, to: 1 },
]
const range = useRouteQuery<string>('range', 'month')
const chosen = computed(() => ranges.find(r => r.value === range.value) ?? ranges[1]!)
const mine = useRouteQuery<string, boolean>('mine', '', { transform: { get: v => v === '1', set: v => v ? '1' : '' } })

const window = computed(() => ({
  from: startOfToday(timeZone.value, chosen.value.from),
  to: startOfToday(timeZone.value, chosen.value.to),
}))
const now = useNow({ interval: 60_000 })

const { data: bookings, isPending, error } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: window.value.from, to: window.value.to, mine: mine.value })),
})
const { data: bookable } = useGetOrgFacilitiesBookable({ path: computed(() => ({ slug: slug.value })) })

const globalFilter = ref('')
const facilityFilter = ref<string[]>([])
const facilityOptions = computed(() => bookable.value?.map(f => f.name) ?? [])

const rows = computed(() => {
  const words = globalFilter.value.trim().toLowerCase()
  return (bookings.value ?? []).filter((b) => {
    if (facilityFilter.value.length && !facilityFilter.value.includes(b.facilityName ?? '')) {
      return false
    }

    return !words || [b.conduct, b.facilityName, b.description, b.pocName, b.pocPhone, b.bookedBy.displayName].some(v => (v ?? '').toLowerCase().includes(words))
  })
})

function sized(column: TableColumn<Booking>, label: string): TableColumn<Booking> {
  return Object.assign(column, {
    header: ({ header }: HeaderContext<Booking, unknown>) => h(TableHeader, { header: header as never, label }),
    meta: {
      style: {
        th: ({ column }: { column: Column<Booking> }) => ({ width: `${column.getSize()}px` }),
      },
    },
  })
}

const columns: TableColumn<Booking>[] = [
  sized({ accessorKey: 'startDateTime', sortingFn: 'datetime', enableResizing: false }, 'When'),
  sized({ accessorKey: 'facilityName', enableResizing: false }, 'Facility'),
  sized({ accessorKey: 'conduct', enableResizing: false }, 'Conduct'),
  sized({ id: 'bookedBy', accessorFn: b => b.bookedBy.displayName, enableResizing: false }, 'Booked by'),
]

function status(booking: Booking) {
  if (booking.endDateTime <= now.value) {
    return 'Over'
  }

  return booking.startDateTime <= now.value ? 'Underway' : undefined
}

function onSelect(_: Event, row: { original: Booking }) {
  router.push(path('bookings', row.original.id))
}

const empty = computed(() => !isPending.value && !error.value && (bookings.value?.length ?? 0) === 0)
const noFacilities = computed(() => bookable.value !== undefined && bookable.value.length === 0)
</script>

<template>
  <UDashboardPanel id="bookings">
    <template #header>
      <AppNavbar title="Bookings">
        <template #trailing>
          <UBadge
            v-if="bookings"
            :label="rows.length"
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
      <TenantSetupChecklist v-if="isAdmin" />

      <UAlert
        v-if="noFacilities && !isAdmin"
        title="You can't book anything yet"
        description="Ask an admin to add facilities, or to give your unit access to them."
        color="warning"
        variant="subtle"
        icon="i-lucide-info"
      />

      <div class="flex flex-wrap items-center gap-2">
        <UInput
          v-model="globalFilter"
          icon="i-lucide-search"
          placeholder="Keyword search"
          aria-label="Keyword search"
          class="w-full sm:w-64"
        />

        <USelect
          v-model="range"
          :items="ranges"
          value-key="value"
          icon="i-lucide-calendar-range"
          aria-label="Which bookings"
          class="w-44"
        />

        <USelectMenu
          v-model="facilityFilter"
          :items="facilityOptions"
          multiple
          icon="i-lucide-building-2"
          placeholder="Any facility"
          aria-label="Filter by facility"
          class="min-w-0 flex-1 sm:w-52 sm:flex-none"
        />

        <USwitch
          v-model="mine"
          label="Only mine"
        />
      </div>

      <UAlert
        v-if="error"
        title="Couldn't load the bookings"
        :description="getErrorReasons(error)[0] ?? 'Try a shorter time, or try again.'"
        color="error"
        variant="subtle"
        icon="i-lucide-circle-alert"
      />

      <UTable
        :data="rows"
        :columns="columns"
        :loading="isPending"
        :get-row-id="(row: Booking) => row.id"
        :empty="empty ? (mine ? 'You have no bookings in this time.' : `Nothing is booked in this time${org ? ` at ${org.name}` : ''}.`) : 'No bookings match.'"
        class="flex-1 min-h-0 rounded-lg border border-default"
        :ui="{
          base: 'border-separate border-spacing-0',
          thead: 'sticky top-0 z-[1] bg-default/90 backdrop-blur',
          th: 'relative border-b border-default',
          td: 'border-b border-default',
          separator: 'hidden',
        }"
        @select="onSelect"
      >
        <template #startDateTime-cell="{ row }">
          <div class="flex flex-col">
            <span class="font-medium text-highlighted">{{ df.format(row.original.startDateTime) }}</span>
            <span class="text-sm text-muted">{{ tf.format(row.original.startDateTime) }} – {{ tf.format(row.original.endDateTime) }}</span>
          </div>
        </template>

        <template #facilityName-cell="{ row }">
          <span class="font-medium text-highlighted">{{ row.original.facilityName }}</span>
        </template>

        <template #conduct-cell="{ row }">
          <div class="flex items-center gap-2">
            <span>{{ row.original.conduct }}</span>
            <UBadge
              v-if="status(row.original)"
              :label="status(row.original)"
              :color="status(row.original) === 'Underway' ? 'success' : 'neutral'"
              variant="subtle"
              size="sm"
            />
          </div>
        </template>

        <template #bookedBy-cell="{ row }">
          <span>{{ row.original.bookedBy.displayName }}</span>
        </template>
      </UTable>
    </template>
  </UDashboardPanel>
</template>
