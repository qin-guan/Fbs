<script setup lang="ts">
import { FilterMatchMode, FilterOperator } from '@primevue/core/api'
import type { FbsWebApiDtosBookingWithUser } from '~/api/models'

definePageMeta({
  layout: 'app',
  middleware: 'admin',
})

const router = useRouter()
const toast = useToast()
const { height } = useWindowSize()

const tableHeight = computed(() => `${height.value - 48}px`)

const { data: bookings, isPending: bookingsIsPending, error: bookingsError } = useAdminBookings()
const { mutate: deleteBooking, isPending: deleteIsPending } = useAdminDeleteBookingMutation()

const deleteConfirmationDialog = ref(false)
const selectedBooking = ref<FbsWebApiDtosBookingWithUser | null>(null)

const defaultFilters = {
  global: { value: null, matchMode: FilterMatchMode.CONTAINS },
  id: { value: null, matchMode: FilterMatchMode.CONTAINS },
  conduct: { value: null, matchMode: FilterMatchMode.CONTAINS },
  facilityName: { value: null, matchMode: FilterMatchMode.IN },
  startDateTime: { operator: FilterOperator.AND, constraints: [{ value: null, matchMode: FilterMatchMode.DATE_IS }] },
  pocName: { value: null, matchMode: FilterMatchMode.CONTAINS },
  pocPhone: { value: null, matchMode: FilterMatchMode.CONTAINS },
  user: { value: null, matchMode: FilterMatchMode.CONTAINS },
}

const filters = ref(defaultFilters)

function clearFilters() {
  filters.value = defaultFilters
}

function onDelete(booking: FbsWebApiDtosBookingWithUser) {
  selectedBooking.value = booking
  deleteConfirmationDialog.value = true
}

function confirmDelete() {
  if (!selectedBooking.value?.id) return
  
  deleteBooking(selectedBooking.value.id, {
    async onSuccess() {
      deleteConfirmationDialog.value = false
      selectedBooking.value = null
      toast.add({
        severity: 'success',
        summary: 'Booking deleted successfully.',
        life: 3000,
      })
    },
    onError() {
      toast.add({
        severity: 'error',
        summary: 'Failed to delete booking.',
        life: 3000,
      })
    }
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
        :style="{ width: '25rem' }"
    >
      <div class="mb-8 gap-3 flex flex-col">
        <span class="text-surface-500 dark:text-surface-400">
          Are you sure you want to delete this booking?
        </span>
        
        <div v-if="selectedBooking" class="bg-surface-50 dark:bg-surface-800 p-3 rounded">
          <p class="text-sm"><strong>Facility:</strong> {{ selectedBooking.facilityName }}</p>
          <p class="text-sm"><strong>Conduct:</strong> {{ selectedBooking.conduct }}</p>
          <p class="text-sm"><strong>Booked by:</strong> {{ selectedBooking.user?.name }} ({{ selectedBooking.user?.phone }})</p>
        </div>

        <Message severity="error">
          This is permanent and will immediately remove the booking from the calendar!
        </Message>
      </div>

      <div class="flex justify-end gap-2">
        <Button
            type="button"
            label="Cancel"
            severity="secondary"
            size="small"
            @click="deleteConfirmationDialog = false"
        />
        <Button
            type="button"
            size="small"
            severity="danger"
            label="Delete"
            :loading="deleteIsPending"
            @click="confirmDelete"
        />
      </div>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <div class="space-x-3 flex items-center">
            <h2>Admin - All Bookings</h2>
            <Button
              type="button"
              label="Clear"
              size="small"
              variant="text"
              @click="clearFilters"
            >
              <template #icon>
                <Icon name="i-lucide-funnel-x" />
              </template>
            </Button>
          </div>
          <div class="flex gap-2">
            <Button
              v-slot="slotProps"
              size="small"
              variant="text"
              as-child
            >
              <NuxtLink
                to="/admin/users"
                :class="slotProps.class"
              >
                Manage Users
              </NuxtLink>
            </Button>
          </div>
        </div>
      </template>
    </AppNavbar>

    <div class="flex-1 overflow-hidden p-4">
      <DataTable
          v-if="bookings"
          :value="bookings"
          paginator
          :rows="20"
          :global-filter-fields="['conduct', 'facilityName', 'pocName', 'user.name', 'user.phone']"
          :filters="filters"
          filter-display="menu"
          :loading="bookingsIsPending"
          :scrollable="true"
          :scroll-height="tableHeight"
          class="w-full"
      >
        <template #empty>
          <div class="text-center py-8">
            <p class="text-surface-500">No bookings found.</p>
          </div>
        </template>

        <Column field="startDateTime" header="Date" sortable>
          <template #body="{ data }">
            {{ data.startDateTime ? new Date(data.startDateTime).toLocaleDateString() : '-' }}
          </template>
          <template #filter="{ filterModel }">
            <Calendar v-model="filterModel.value" date-format="mm/dd/yy" placeholder="mm/dd/yyyy" />
          </template>
        </Column>

        <Column field="facilityName" header="Facility" sortable>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search facility" />
          </template>
        </Column>

        <Column field="conduct" header="Conduct" sortable>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search conduct" />
          </template>
        </Column>

        <Column field="pocName" header="POC Name">
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search POC name" />
          </template>
        </Column>

        <Column header="Booked By" field="user.name">
          <template #body="{ data }">
            <div>
              <p class="font-semibold">{{ data.user?.name }}</p>
              <p class="text-sm text-surface-500">{{ data.user?.phone }}</p>
              <p class="text-xs text-surface-400">{{ data.user?.unit }}</p>
            </div>
          </template>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search user" />
          </template>
        </Column>

        <Column header="Actions" :frozen="true" align-frozen="right" style="width: 8rem">
          <template #body="{ data }">
            <div class="flex gap-2">
              <Button
                icon="i-lucide-edit"
                size="small"
                variant="text"
                @click="onEdit(data)"
              />
              <Button
                icon="i-lucide-trash-2"
                size="small"
                severity="danger"
                variant="text"
                @click="onDelete(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>

      <div v-else-if="bookingsError" class="text-center py-8">
        <Message severity="error">
          Failed to load bookings. Please check your permissions.
        </Message>
      </div>
    </div>
  </div>
</template>
