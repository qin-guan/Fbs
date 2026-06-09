<script setup lang="ts">
import type { FbsWebApiDtosBookingWithUser } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const route = useRoute()
const router = useRouter()
const toast = useToast()

const bookingId = computed(() => route.params.id as string)
const { data: booking, isPending: bookingIsPending, error: bookingError } = useAdminBooking(bookingId)
const { data: nominalRoll, isPending: nominalRollIsPending } = useNominalRollMapping()
const nominalRollMiniSearch = useNominalRollMiniSearch()
const { mutate: updateMutate, isPending: updateIsPending } = useAdminUpdateBookingMutation()
const { mutate: deleteMutate, isPending: deleteIsPending } = useAdminDeleteBookingMutation()

const updateValues = ref<Partial<FbsWebApiDtosBookingWithUser>>({})
const deleteConfirmationDialog = ref(false)
const filteredItemsPhone = ref<string[]>([])
const filteredItemsName = computed(() => {
  return filteredItemsPhone.value.map(i => nominalRoll.value?.[i]).filter(Boolean)
})

watch(booking, (newBooking) => {
  if (!newBooking) return
  updateValues.value = { ...newBooking }
}, { immediate: true })

function normalizePhone(value?: string | null) {
  if (!value) return undefined
  const digits = value.replace(/\D/g, '')
  return digits.startsWith('65') ? digits : `65${digits}`
}

function optionSelect({ value }: { value: string }) {
  if (!nominalRoll.value) return

  const phone = Object.entries(nominalRoll.value).find(entry => entry[1] === value)?.[0]
  if (!phone) return

  updateValues.value.pocName = value
  updateValues.value.pocPhone = phone
}

function searchItems(event: { query: string }) {
  filteredItemsPhone.value = nominalRollMiniSearch.value?.search(event.query).map((entry: { id: string }) => entry.id) ?? []
}

function formatDate(value?: Date | string | null) {
  if (!value) return '-'
  return new Date(value).toLocaleDateString()
}

function formatTime(value?: Date | string | null) {
  if (!value) return '-'
  return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function updateBooking() {
  if (!updateValues.value.id) return

  updateMutate(
    {
      bookingId: updateValues.value.id,
      data: {
        conduct: updateValues.value.conduct,
        description: updateValues.value.description,
        pocName: updateValues.value.pocName,
        pocPhone: normalizePhone(updateValues.value.pocPhone),
      },
    },
    {
      async onSuccess() {
        toast.add({
          severity: 'success',
          summary: 'Booking updated successfully.',
          life: 3000,
        })
        await router.push('/admin/bookings')
      },
      onError(error) {
        toast.add({
          severity: 'error',
          summary: 'Failed to update booking.',
          detail: error?.message ?? 'Please try again.',
          life: 4000,
        })
      },
    },
  )
}

function confirmDelete() {
  if (!updateValues.value.id) return

  deleteMutate(updateValues.value.id, {
    async onSuccess() {
      toast.add({
        severity: 'success',
        summary: 'Booking deleted successfully.',
        life: 3000,
      })
      deleteConfirmationDialog.value = false
      await router.push('/admin/bookings')
    },
    onError() {
      toast.add({
        severity: 'error',
        summary: 'Failed to delete booking.',
        detail: 'Please check your admin permissions and try again.',
        life: 4000,
      })
    },
  })
}
</script>

<template>
  <div class="h-full flex flex-col">
    <Dialog
      v-model:visible="deleteConfirmationDialog"
      modal
      header="Delete booking"
      :style="{ width: '28rem' }"
    >
      <div class="mb-6 flex flex-col gap-3">
        <p class="text-surface-500">
          Are you sure you want to delete this booking?
        </p>

        <div class="rounded border border-surface-200 bg-surface-50 p-3">
          <p class="text-sm font-semibold">
            {{ updateValues.facilityName || '-' }}
          </p>
          <p class="text-sm">
            {{ updateValues.conduct || 'No conduct' }}
          </p>
          <p class="text-sm text-surface-500">
            {{ updateValues.pocName || '-' }} {{ updateValues.pocPhone ? `(${updateValues.pocPhone})` : '' }}
          </p>
        </div>

        <Message severity="error">
          This is permanent and will immediately remove the booking from the calendar.
        </Message>
      </div>

      <template #footer>
        <Button
          type="button"
          label="Cancel"
          severity="secondary"
          size="small"
          @click="deleteConfirmationDialog = false"
        />
        <Button
          type="button"
          label="Delete"
          severity="danger"
          size="small"
          icon="i-lucide-trash-2"
          :loading="deleteIsPending"
          @click="confirmDelete"
        />
      </template>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex items-center justify-between gap-3 pr-3">
          <Breadcrumb
            :pt="{ root: { style: 'padding: 0;' } }"
            :model="[
              { label: 'Admin Bookings', route: '/admin/bookings' },
              { label: booking?.facilityName ?? 'Booking', route: `/admin/bookings/${bookingId}` },
            ]"
          >
            <template #item="{ item, props }">
              <NuxtLink
                v-if="item.route"
                v-slot="{ href, navigate }"
                :to="item.route"
                custom
              >
                <a
                  :href="href"
                  v-bind="props.action"
                  @click="navigate"
                >
                  {{ item.label }}
                </a>
              </NuxtLink>
            </template>
          </Breadcrumb>
        </div>
      </template>
    </AppNavbar>

    <main class="flex-1 overflow-auto p-4">
      <Message
        v-if="bookingError"
        severity="error"
        class="mb-3"
      >
        Failed to load booking. Please check your admin permissions.
      </Message>

      <Card
        v-if="booking"
        class="max-w-3xl"
      >
        <template #title>
          Booking Details
        </template>
        <template #subtitle>
          {{ booking.facilityName }}
        </template>

        <template #content>
          <div class="mb-6 grid gap-4 sm:grid-cols-2">
            <div>
              <p class="mb-1 text-sm text-surface-500">
                Booked By
              </p>
              <p class="font-semibold">
                {{ booking.user?.name || '-' }}
              </p>
              <p class="text-sm text-surface-500">
                {{ booking.user?.phone || '-' }} {{ booking.user?.unit ? `(${booking.user.unit})` : '' }}
              </p>
            </div>

            <div>
              <p class="mb-1 text-sm text-surface-500">
                Date and Time
              </p>
              <p class="font-semibold">
                {{ formatDate(booking.startDateTime) }}
              </p>
              <p class="text-sm text-surface-500">
                {{ formatTime(booking.startDateTime) }} - {{ formatTime(booking.endDateTime) }}
              </p>
            </div>
          </div>

          <Divider />

          <form
            class="flex flex-col gap-4"
            @submit.prevent="updateBooking"
          >
            <FloatLabel variant="on">
              <InputText
                id="facility"
                v-model="updateValues.facilityName"
                disabled
                fluid
              />
              <label for="facility">Facility</label>
            </FloatLabel>

            <FloatLabel variant="on">
              <InputText
                id="conduct"
                v-model="updateValues.conduct"
                fluid
              />
              <label for="conduct">Conduct</label>
            </FloatLabel>

            <FloatLabel variant="on">
              <Textarea
                id="description"
                v-model="updateValues.description"
                rows="4"
                auto-resize
                fluid
              />
              <label for="description">Description</label>
            </FloatLabel>

            <FloatLabel variant="on">
              <AutoComplete
                id="pocName"
                v-model="updateValues.pocName"
                :suggestions="filteredItemsName"
                :loading="nominalRollIsPending"
                dropdown
                fluid
                @complete="searchItems"
                @option-select="optionSelect"
              />
              <label for="pocName">POC Name</label>
            </FloatLabel>

            <FloatLabel variant="on">
              <InputText
                id="pocPhone"
                v-model="updateValues.pocPhone"
                type="tel"
                fluid
              />
              <label for="pocPhone">POC Phone</label>
            </FloatLabel>

            <div class="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                label="Cancel"
                severity="secondary"
                @click="router.push('/admin/bookings')"
              />
              <Button
                type="button"
                label="Delete"
                severity="danger"
                icon="i-lucide-trash-2"
                :loading="deleteIsPending"
                @click="deleteConfirmationDialog = true"
              />
              <Button
                type="submit"
                label="Save"
                icon="i-lucide-save"
                :loading="updateIsPending"
              />
            </div>
          </form>
        </template>
      </Card>

      <div
        v-else-if="bookingIsPending"
        class="max-w-3xl"
      >
        <Skeleton
          height="3rem"
          class="mb-4"
        />
        <Skeleton height="24rem" />
      </div>
    </main>
  </div>
</template>
