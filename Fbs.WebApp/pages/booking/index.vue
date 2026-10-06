<script setup lang="ts">
import { h } from 'vue'
import { getLocalTimeZone, today, type CalendarDate } from '@internationalized/date'
import type { TableColumn } from '@nuxt/ui'
import type { Column, ColumnFiltersState, FilterFn, HeaderContext } from '@tanstack/vue-table'
import { useGetBooking, useGetFacility, type FbsWebApiDtosBookingWithUser } from '~/api'
import TableHeader from '~/components/table-header.vue'

definePageMeta({
  layout: 'app',
})

type Booking = FbsWebApiDtosBookingWithUser

const router = useRouter()
const { df, tf } = useFormatter()

const { data: facilities } = useGetFacility()
const { data: bookings, isPending: bookingsIsPending } = useGetBooking()

// Keyboard shortcuts: alt+n for a new booking, / to focus the keyword search
const activeElement = useActiveElement()
const notUsingInput = computed(() => {
  const el = activeElement.value
  return !(el?.tagName === 'INPUT' || el?.tagName === 'TEXTAREA' || el?.isContentEditable)
})

const keys = useMagicKeys({
  passive: false,
  onEventFired(e) {
    if (e.key === '/' && e.type === 'keydown' && notUsingInput.value) {
      e.preventDefault()
    }
  },
})

const keywordSearchInput = useTemplateRef('keywordSearchInput')

whenever(() => !!keys['Alt_KeyN']?.value, async () => {
  await router.push('/booking/new')
})

whenever(() => !!keys['slash']?.value && notUsingInput.value, () => {
  keywordSearchInput.value?.inputRef?.focus()
})

// Filters
const globalFilter = ref('')
const columnFilters = ref<ColumnFiltersState>([])
const columnSizing = ref<Record<string, number>>({})

// UTable only re-computes its rows when table state changes, not when `data` does,
// so hand it a fresh filter array once it has received newly (re)fetched bookings.
watch(bookings, () => {
  columnFilters.value = [...columnFilters.value]
}, { flush: 'post' })

const globalFilterFields = ['id', 'conduct', 'facilityName', 'pocName', 'pocPhone'] as const

const facilityOptions = computed(() => facilities.value?.map(f => f.name).filter((n): n is string => !!n) ?? [])

const facilityFilter = computed<string[]>({
  get: () => (columnFilters.value.find(f => f.id === 'facilityName')?.value as string[] | undefined) ?? [],
  set: value => setColumnFilter('facilityName', value.length ? value : undefined),
})

const startDateFilter = computed<CalendarDate | undefined>({
  get: () => columnFilters.value.find(f => f.id === 'startDateTime')?.value as CalendarDate | undefined,
  set: value => setColumnFilter('startDateTime', value),
})

const hasFilters = computed(() => !!globalFilter.value || columnFilters.value.length > 0)

function setColumnFilter(id: string, value: unknown) {
  const others = columnFilters.value.filter(f => f.id !== id)
  columnFilters.value = value === undefined ? others : [...others, { id, value }]
}

function clearFilters() {
  globalFilter.value = ''
  columnFilters.value = []
}

const globalFilterFn: FilterFn<Booking> = (row, _columnId, value: string) => {
  const query = value.toLowerCase()
  return globalFilterFields.some(field => String(row.original[field] ?? '').toLowerCase().includes(query))
}

const facilityFilterFn: FilterFn<Booking> = (row, columnId, value: string[]) => {
  return !value?.length || value.includes(row.getValue<string>(columnId))
}

const startDateFilterFn: FilterFn<Booking> = (row, columnId, value: CalendarDate) => {
  const date = row.getValue<Date | undefined>(columnId)
  if (!value || !date) return !value
  return date.getFullYear() === value.year && date.getMonth() + 1 === value.month && date.getDate() === value.day
}

// Columns
function durationInHours(booking: Booking) {
  if (!booking.startDateTime || !booking.endDateTime) return undefined
  return (booking.endDateTime.getTime() - booking.startDateTime.getTime()) / (1000 * 60 * 60)
}

// Once a column has been resized, pin its width and truncate overflowing text
function sizedColumn(column: TableColumn<Booking>, label: string): TableColumn<Booking> {
  const style = ({ column }: { column: Column<Booking> }): Record<string, string> => columnSizing.value[column.id]
    ? { width: `${column.getSize()}px`, minWidth: `${column.getSize()}px`, maxWidth: `${column.getSize()}px` }
    : {}

  return Object.assign(column, {
    header: ({ header }: HeaderContext<Booking, unknown>) => h(TableHeader, { header, label }),
    meta: {
      style: { th: style, td: style },
      class: { td: ({ column }: { column: Column<Booking> }) => columnSizing.value[column.id] ? 'truncate' : '' },
    },
  })
}

const columns: TableColumn<Booking>[] = [
  sizedColumn({ accessorKey: 'id', enableSorting: false }, 'ID'),
  sizedColumn({ accessorKey: 'facilityName', filterFn: facilityFilterFn }, 'Facility'),
  sizedColumn({ accessorKey: 'conduct' }, 'Conduct'),
  sizedColumn({ id: 'duration', accessorFn: durationInHours, enableSorting: false }, 'Duration'),
  sizedColumn({ accessorKey: 'startDateTime', sortingFn: 'datetime', filterFn: startDateFilterFn }, 'Start'),
  sizedColumn({ id: 'poc', accessorFn: b => `${b.pocName} (${b.pocPhone})`, enableSorting: false }, 'POC'),
]

function onSelect(_: Event, row: { original: Booking }) {
  if (row.original.id) {
    router.push(`/booking/${row.original.id}`)
  }
}
</script>

<template>
  <UDashboardPanel id="bookings">
    <template #header>
      <AppNavbar title="Bookings">
        <template #trailing>
          <UBadge
            v-if="bookings"
            :label="bookings.length"
            variant="subtle"
          />
        </template>

        <template #right>
          <UButton
            to="/booking/new"
            icon="i-lucide-plus"
            label="New"
          >
            <template #trailing>
              <span class="hidden sm:inline-flex items-center gap-0.5">
                <UKbd
                  value="alt"
                  size="sm"
                  class="bg-white/20 text-inverted ring-0"
                />
                <UKbd
                  value="N"
                  size="sm"
                  class="bg-white/20 text-inverted ring-0"
                />
              </span>
            </template>
          </UButton>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="flex flex-wrap items-center gap-2">
        <UInput
          ref="keywordSearchInput"
          v-model="globalFilter"
          icon="i-lucide-search"
          placeholder="Keyword search"
          aria-label="Keyword search"
          class="w-full sm:w-72"
        >
          <template #trailing>
            <UKbd
              value="/"
              class="hidden sm:inline-flex"
            />
          </template>
        </UInput>

        <USelectMenu
          v-model="facilityFilter"
          :items="facilityOptions"
          multiple
          icon="i-lucide-building-2"
          placeholder="Any facility"
          aria-label="Filter by facility"
          class="min-w-0 flex-1 sm:w-56 sm:flex-none"
        />

        <UPopover>
          <UButton
            color="neutral"
            variant="outline"
            icon="i-lucide-calendar"
            :label="startDateFilter ? df.format(startDateFilter.toDate(getLocalTimeZone())) : 'Any date'"
            aria-label="Filter by start date"
          />

          <template #content="{ close }">
            <div class="p-2">
              <UCalendar
                v-model="startDateFilter"
                @update:model-value="close"
              />
              <div class="flex justify-between gap-2 border-t border-default pt-2">
                <UButton
                  label="Today"
                  color="neutral"
                  variant="ghost"
                  size="sm"
                  @click="startDateFilter = today(getLocalTimeZone()); close()"
                />
                <UButton
                  label="Clear"
                  color="neutral"
                  variant="ghost"
                  size="sm"
                  :disabled="!startDateFilter"
                  @click="startDateFilter = undefined; close()"
                />
              </div>
            </div>
          </template>
        </UPopover>

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
      </div>

      <UTable
        v-model:global-filter="globalFilter"
        v-model:column-filters="columnFilters"
        v-model:column-sizing="columnSizing"
        :data="bookings ?? []"
        :columns="columns"
        :loading="bookingsIsPending"
        :global-filter-options="{ globalFilterFn }"
        :column-sizing-options="{ enableColumnResizing: true, columnResizeMode: 'onChange' }"
        :get-row-id="(row: Booking) => row.id ?? ''"
        :virtualize="{ estimateSize: 53 }"
        empty="No bookings."
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
        <template #id-cell="{ row }">
          <ULink
            :to="`/booking/${row.original.id}`"
            class="font-mono text-primary hover:underline"
          >
            {{ row.original.id?.substring(0, 8) }}
          </ULink>
        </template>

        <template #facilityName-cell="{ row }">
          <span class="font-medium text-highlighted">{{ row.original.facilityName }}</span>
        </template>

        <template #duration-cell="{ getValue }">
          {{ getValue() }} {{ getValue() === 1 ? 'hour' : 'hours' }}
        </template>

        <template #startDateTime-cell="{ row }">
          <template v-if="row.original.startDateTime">
            {{ df.format(row.original.startDateTime) }}, {{ tf.format(row.original.startDateTime) }}
          </template>
        </template>

        <template #poc-cell="{ row }">
          {{ row.original.pocName }} <span class="text-dimmed">({{ row.original.pocPhone }})</span>
        </template>
      </UTable>
    </template>
  </UDashboardPanel>
</template>
