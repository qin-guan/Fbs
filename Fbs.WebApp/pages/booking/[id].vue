<script setup lang="ts">
import { useMutation, useQuery } from '@urql/vue'
import type { BreadcrumbItem } from '@nuxt/ui'

const id = useRoute().params.id as string

const toast = useToast()

const bookingQuery = useQuery({
  query: queries.bookings,
  variables: {
    id,
  },
})

const facilities = useQuery({
  query: queries.facilities,
  variables: {},
})

const booking = computed(() => bookingQuery.data?.value?.bookings.at(0))
const updateBookingMutation = useMutation(mutations.updateBooking)
const deleteBookingMutation = useMutation(mutations.deleteBooking)

const modifiedBooking = reactive({
  pending: false,
  conduct: '',
  description: '',
  facilityName: '',
  pocName: '',
  pocPhone: '',
})

whenever(booking, (value) => {
  modifiedBooking.conduct = value.conduct ?? ''
  modifiedBooking.description = value.description ?? ''
  modifiedBooking.facilityName = value.facilityName ?? ''
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
  deleteBookingMutation.executeMutation({ id }, { additionalTypenames: ['Booking'] })
}
</script>

<template>
  <div class="p-4">
    <UBreadcrumb :items="items" />

    <form
      class="mt-4 space-y-4"
      @submit.prevent="update"
    >
      <h1 class="font-semibold text-xl">
        {{ booking?.conduct }}
      </h1>

      <UFormField
        label="Facility"
        name="facility"
      >
        <USelectMenu
          v-model="modifiedBooking.facilityName"
          placeholder="Select facility"
          class="w-64"
          :items="facilities.data.value?.facilities"
          label-key="name"
          value-key="name"
          :search-input="{
            placeholder: 'Search facilities...',
          }"
        />
      </UFormField>

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
  </div>
</template>
