<script setup lang="ts">
import { useMutation, useQuery } from '@urql/vue'
import type { BreadcrumbItem } from '@nuxt/ui'

definePageMeta({
  layout: 'app',
})

const id = useRoute().params.id as string

const toast = useToast()

const bookingQuery = useQuery({
  query: queries.booking,
  variables: {
    id,
  },
})

const booking = computed(() => bookingQuery.data?.value?.booking)
const updateBookingMutation = useMutation(mutations.updateBooking)
const deleteBookingMutation = useMutation(mutations.deleteBooking)

const modifiedBooking = reactive({
  pending: false,
  conduct: '',
  description: '',
  pocName: '',
  pocPhone: '',
})

whenever(booking, (value) => {
  modifiedBooking.conduct = value.conduct ?? ''
  modifiedBooking.description = value.description ?? ''
  modifiedBooking.pocName = value.pocName ?? ''
  modifiedBooking.pocPhone = value.pocPhone ?? ''
}, { immediate: true })

const items = computed<BreadcrumbItem[]>(() => [
  {
    label: 'Bookings',
    icon: 'i-lucide-table',
    to: '/booking',
  },
  {
    label: booking.value?.conduct ?? 'Loading...',
    icon: 'i-lucide-house',
  },
])

function update() {
  modifiedBooking.pending = true
  updateBookingMutation.executeMutation({ id, ...modifiedBooking })
    .then(({ error }) => {
      if (error) {
        error.graphQLErrors.forEach((error) => {
          toast.add({
            color: 'error',
            title: 'Error',
            description: error.extensions?.message as string ?? error.message ?? 'Unknown error occurred.',
          })
        })
      }
    })
    .finally(() => {
      modifiedBooking.pending = false
    })
}

function deleteBooking() {
  modifiedBooking.pending = true
  deleteBookingMutation.executeMutation({ id }, { additionalTypenames: ['Booking'] })
    .then(({ error }) => {
      if (!error) {
        navigateTo('/booking')
      }
      else {
        error.graphQLErrors.forEach((error) => {
          toast.add({
            color: 'error',
            title: 'Error',
            description: error.extensions?.message as string ?? error.message ?? 'Unknown error occurred.',
          })
        })
      }
    })
    .finally(() => {
      modifiedBooking.pending = false
    })
}
</script>

<template>
  <UDashboardPanel>
    <template #header>
      <UDashboardNavbar title="Upcoming bookings">
        <template #toggle>
          <UDashboardSidebarToggle />
        </template>
        <template #left>
          <UBreadcrumb :items="items" />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <form
        class="mt-4 space-y-4"
        @submit.prevent="update"
      >
        <h1 class="font-semibold text-xl">
          {{ booking?.conduct }}
        </h1>

        <UAlert
          title="Heads up!"
          description="To update the timing or facility, please delete and create a new booking."
          icon="i-lucide-info"
        />

        <UFormField
          label="Conduct"
          name="conduct"
        >
          <UInput
            v-model="modifiedBooking.conduct"
            autofocus
          />
        </UFormField>

        <UFormField
          label="Description"
          name="description"
        >
          <UTextarea
            v-model="modifiedBooking.description"
            autoresize
          />
        </UFormField>

        <UFormField
          label="PoC Name"
          name="pocName"
        >
          <UInput v-model="modifiedBooking.pocName" />
        </UFormField>

        <UFormField
          label="PoC Phone"
          name="pocPhone"
        >
          <UInput v-model="modifiedBooking.pocPhone" />
        </UFormField>

        <div class="flex gap-3">
          <UButton
            :loading="modifiedBooking.pending"
            type="submit"
          >
            Update
          </UButton>

          <UButton
            :loading="modifiedBooking.pending"
            color="error"
            variant="ghost"
            @click="deleteBooking"
          >
            Delete
          </UButton>
        </div>
      </form>
    </template>
  </UDashboardPanel>
</template>
