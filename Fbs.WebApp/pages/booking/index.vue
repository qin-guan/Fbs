<script setup lang="ts">
import { h } from 'vue'
import type { DropdownMenuItem, TableColumn } from '@nuxt/ui'
import { useQuery } from '@urql/vue'
import type { BookingsQuery } from '~/gql/graphql'
import { ULink } from '#components'

definePageMeta({
  layout: 'app',
})

const onlyUpcoming = ref(false)
const today = new Date()
today.setHours(0, 0, 0, 0)

const { data: bookings, fetching: bookingsFetching } = useQuery({
  query: queries.bookings,
  variables: computed(() => ({
    startsAfter: onlyUpcoming.value ? today.toISOString() : undefined,
  })),
})

const globalFilter = ref('')

const columns: TableColumn<BookingsQuery['bookings'][0]>[] = [
  {
    accessorKey: 'id',
    header: 'ID',
    cell({ row }) {
      return h(ULink, { as: 'a', to: `/booking/${row.original.id}` }, () => row.original.id?.substring(row.original.id.length - 8, row.original.id.length))
    },
  },
  {
    accessorKey: 'conduct',
    header: 'Conduct',
  },
  {
    accessorKey: 'facilityName',
    header: 'Facility',
  },
  {
    accessorKey: 'startDateTime',
    header: 'Start',
    cell({ row }) {
      return new Date(row.original.startDateTime).toLocaleString()
    },
  },
  {
    accessorKey: 'endDateTime',
    header: 'End',
    cell({ row }) {
      return new Date(row.original.endDateTime).toLocaleString()
    },
  },
  {
    accessorKey: 'pocName',
    header: 'PoC',
    cell({ row }) {
      return row.original.pocName + ' (' + row.original.pocPhone + ')'
    },
  },
]

const items = computed(() => [
  {
    label: 'All',
    checked: !onlyUpcoming.value,
    type: 'checkbox',
    onUpdateChecked(_: boolean) {
      onlyUpcoming.value = false
    },
  },
  {
    label: 'Only upcoming',
    checked: onlyUpcoming.value,
    type: 'checkbox',
    onUpdateChecked(_: boolean) {
      onlyUpcoming.value = true
    },
  },
] satisfies DropdownMenuItem[])
</script>

<template>
  <UDashboardPanel :ui="{ body: 'p-0!' }">
    <template #header>
      <UDashboardNavbar title="Upcoming bookings">
        <template #toggle>
          <UDashboardSidebarToggle />
        </template>
        <template #right>
          <div class="flex gap-3 items-center">
            <UDropdownMenu
              :items="items"
              :content="{ align: 'start' }"
            >
              <UButton
                :label="onlyUpcoming ? 'Only upcoming' : 'All'"
                color="neutral"
                variant="outline"
                icon="i-lucide-filter"
              />
            </UDropdownMenu>

            <UInput
              v-model="globalFilter"
              class="max-w-sm"
              placeholder="Filter..."
            />

            <BookingNewModalButton />
          </div>
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UTable
        v-if="!bookingsFetching"
        :data="bookings?.bookings"
        :global-filter="globalFilter"
        :columns="columns"
      />
    </template>
  </UDashboardPanel>
</template>
