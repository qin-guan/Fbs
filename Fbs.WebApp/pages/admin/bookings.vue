<script setup lang="ts">
import { FilterMatchMode, FilterOperator } from '@primevue/core/api'
import type { FbsWebApiDtosBookingWithUser } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const router = useRouter()
const toast = useToast()
const { height } = useWindowSize()

const tableHeight = computed(() => `${Math.max(height.value - 180, 320)}px`)

const { data: bookings, isPending: bookingsIsPending, error: bookingsError } = useAdminBookings()
const { mutate: deleteBooking, isPending: deleteIsPending } = useAdminDeleteBookingMutation()

const deleteConfirmationDialog = ref(false)
const selectedBooking = ref<FbsWebApiDtosBookingWithUser | null>(null)

function createDefaultFilters() {
  return {
    global: { value: null, matchMode: FilterMatchMode.CONTAINS },
    id: { value: null, matchMode: FilterMatchMode.CONTAINS },
    conduct: { value: null, matchMode: FilterMatchMode.CONTAINS },
    facilityName: { value: null, matchMode: FilterMatchMode.CONTAINS },
    startDateTime: { operator: FilterOperator.AND, constraints: [{ value: null, matchMode: FilterMatchMode.DATE_IS }] },
    pocName: { value: null, matchMode: FilterMatchMode.CONTAINS },
    pocPhone: { value: null, matchMode: FilterMatchMode.CONTAINS },
    'user.name': { value: null, matchMode: FilterMatchMode.CONTAINS },
    'user.phone': { value: null, matchMode: FilterMatchMode.CONTAINS },
    'user.unit': { value: null, matchMode: FilterMatchMode.CONTAINS },
  }
}

const filters = ref(createDefaultFilters())

function clearFilters() {
  filters.value = createDefaultFilters()
}

function formatDate(value?: Date | string | null) {
  if (!value) return '-'
  return new Date(value).toLocaleDateString()
}

function formatTimeRange(booking: FbsWebApiDtosBookingWithUser) {
  if (!booking.startDateTime || !booking.endDateTime) return '-'

  return `${new Date(booking.startDateTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} - ${new Date(booking.endDateTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
}

function onDelete(booking: FbsWebApiDtosBookingWithUser) {
  selectedBooking.value = booking
  deleteConfirmationDialog.value = true
}

function confirmDelete() {
  if (!selectedBooking.value?.id) return

  deleteBooking(selectedBooking.value.id, {
    onSuccess() {
      deleteConfirmationDialog.value = false
      selectedBooking.value = null
      toast.add({
        severity: 'success',
        summary: 'Booking deleted successfully.',
        detail: 'The booking was removed from the calendar.',
        life: 3000,
      })
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

function onEdit(booking: FbsWebApiDtosBookingWithUser) {
  if (!booking.id) return
  router.push(`/admin/bookings/${booking.id}`)
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

        <div
          v-if="selectedBooking"
          class="rounded border border-surface-200 bg-surface-50 p-3"
        >
          <p class="text-sm font-semibold">
            {{ selectedBooking.facilityName }}
          </p>
          <p class="text-sm">
            {{ selectedBooking.conduct || 'No conduct' }}
          </p>
          <p class="text-sm text-surface-500">
            {{ selectedBooking.user?.name || 'Unknown user' }} {{ selectedBooking.user?.phone ? `(${selectedBooking.user.phone})` : '' }}
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
          <div class="flex min-w-0 items-center gap-3">
            <h2 class="truncate text-base font-semibold">
              Admin - All Bookings
            </h2>
            <Button
              type="button"
              label="Clear"
              size="small"
              severity="secondary"
              variant="text"
              @click="clearFilters"
            >
              <template #icon>
                <Icon name="i-lucide-funnel-x" />
              </template>
            </Button>
          </div>

          <Button
            v-slot="slotProps"
            as-child
            size="small"
            severity="secondary"
            variant="outlined"
          >
            <NuxtLink
              to="/admin/users"
              :class="slotProps.class"
            >
              <Icon name="i-lucide-users" />
              <span>Manage Users</span>
            </NuxtLink>
          </Button>
        </div>
      </template>
    </AppNavbar>

    <main class="flex-1 overflow-hidden p-4">
      <Message
        v-if="bookingsError"
        severity="error"
        class="mb-3"
      >
        Failed to load bookings. Please check your admin permissions.
      </Message>

      <DataTable
        v-model:filters="filters"
        :value="bookings ?? []"
        paginator
        :rows="20"
        :rows-per-page-options="[10, 20, 50, 100]"
        :global-filter-fields="['conduct', 'facilityName', 'pocName', 'pocPhone', 'user.name', 'user.phone', 'user.unit']"
        filter-display="menu"
        :loading="bookingsIsPending"
        scrollable
        :scroll-height="tableHeight"
        data-key="id"
        class="w-full"
      >
        <template #header>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <IconField>
              <InputIcon>
                <Icon name="i-lucide-search" />
              </InputIcon>
              <InputText
                v-model="filters.global.value"
                placeholder="Search bookings"
              />
            </IconField>

            <Tag
              :value="`${bookings?.length ?? 0} bookings`"
              severity="secondary"
            />
          </div>
        </template>

        <template #empty>
          <div class="py-8 text-center text-surface-500">
            No bookings found.
          </div>
        </template>

        <Column
          field="startDateTime"
          header="Date"
          sortable
        >
          <template #body="{ data }">
            <div>
              <p class="font-semibold">
                {{ formatDate(data.startDateTime) }}
              </p>
              <p class="text-xs text-surface-500">
                {{ formatTimeRange(data) }}
              </p>
            </div>
          </template>
          <template #filter="{ filterModel }">
            <DatePicker
              v-model="filterModel.value"
              date-format="mm/dd/yy"
              placeholder="mm/dd/yyyy"
            />
          </template>
        </Column>

        <Column
          field="facilityName"
          header="Facility"
          sortable
        >
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search facility"
            />
          </template>
        </Column>

        <Column
          field="conduct"
          header="Conduct"
          sortable
        >
          <template #body="{ data }">
            {{ data.conduct || '-' }}
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search conduct"
            />
          </template>
        </Column>

        <Column
          field="pocName"
          header="POC"
        >
          <template #body="{ data }">
            <div>
              <p>{{ data.pocName || '-' }}</p>
              <p class="text-xs text-surface-500">
                {{ data.pocPhone || '-' }}
              </p>
            </div>
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search POC"
            />
          </template>
        </Column>

        <Column
          header="Booked By"
          field="user.name"
        >
          <template #body="{ data }">
            <div>
              <p class="font-semibold">
                {{ data.user?.name || '-' }}
              </p>
              <p class="text-sm text-surface-500">
                {{ data.user?.phone || '-' }}
              </p>
              <p class="text-xs text-surface-400">
                {{ data.user?.unit || '-' }}
              </p>
            </div>
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search user"
            />
          </template>
        </Column>

        <Column
          header="Actions"
          :frozen="true"
          align-frozen="right"
          style="width: 7rem"
        >
          <template #body="{ data }">
            <div class="flex gap-1">
              <Button
                icon="i-lucide-pencil"
                size="small"
                severity="secondary"
                variant="text"
                aria-label="Edit booking"
                @click="onEdit(data)"
              />
              <Button
                icon="i-lucide-trash-2"
                size="small"
                severity="danger"
                variant="text"
                aria-label="Delete booking"
                @click="onDelete(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </main>
  </div>
</template>
