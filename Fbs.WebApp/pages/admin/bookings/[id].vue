<script setup lang="ts">
import type { FbsWebApiDtosBookingWithUser } from '~/api/models'

definePageMeta({
  layout: 'app',
  middleware: 'admin',
})

const route = useRoute()
const router = useRouter()
const toast = useToast()

const bookingId = route.params.id as string
const { data: booking } = useBooking(bookingId)
const { data: nominalRoll, isPending: nominalRollIsPending } = useNominalRollMapping()
const nominalRollMiniSearch = useNominalRollMiniSearch()
const { mutate: updateMutate, isPending: updateIsPending } = useAdminUpdateBookingMutation()

const updateValues = ref<FbsWebApiDtosBookingWithUser>({})
const filteredItemsPhone = ref<string[]>([])
const filteredItemsName = computed(() => {
  return filteredItemsPhone.value.map(i => nominalRoll.value[i])
})

whenever(booking, (newBooking) => {
  updateValues.value = { ...newBooking }
})

function optionSelect({ value }: any) {
  if (!nominalRoll.value) return
  updateValues.value.pocPhone = Object.entries(nominalRoll.value).find(e => e[1] == value)?.[0].slice(2) ?? ''
}

function searchItems(event: any) {
  filteredItemsPhone.value = nominalRollMiniSearch.value?.search(event.query).map((e: any) => e.id) ?? []
}

function updateBooking() {
  if (!updateValues.value.id) return
  
  updateMutate(
    {
      bookingId: updateValues.value.id as string,
      data: {
        conduct: updateValues.value.conduct,
        description: updateValues.value.description,
        pocName: updateValues.value.pocName,
        pocPhone: '65' + updateValues.value.pocPhone,
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
    }
  )
}

function goBack() {
  router.push('/admin/bookings')
}
</script>

<template>
  <div class="h-full flex flex-col">
    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <Breadcrumb
              :pt="{ root: { style: 'padding: 0;' } }"
              :model="[
              { label: 'Admin Bookings', route: '/admin/bookings' },
              { label: booking?.facilityName ?? 'Loading...', route: `/admin/bookings/${booking?.id}` },
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

    <div class="flex-1 overflow-auto p-4">
      <div class="max-w-2xl">
        <Card v-if="booking">
          <template #content>
            <!-- Booking Info -->
            <div class="mb-6">
              <h3 class="text-lg font-semibold mb-4">Booking Details</h3>
              
              <div class="grid grid-cols-2 gap-4 mb-4">
                <div>
                  <p class="text-sm text-surface-500 mb-1">Booked By</p>
                  <p class="font-semibold">{{ booking.user?.name }}</p>
                  <p class="text-sm text-surface-400">{{ booking.user?.phone }} ({{ booking.user?.unit }})</p>
                </div>
                <div>
                  <p class="text-sm text-surface-500 mb-1">Date</p>
                  <p class="font-semibold">
                    {{ booking.startDateTime ? new Date(booking.startDateTime).toLocaleDateString() : '-' }}
                  </p>
                  <p class="text-sm text-surface-400">
                    {{ booking.startDateTime ? new Date(booking.startDateTime).toLocaleTimeString() : '-' }} -
                    {{ booking.endDateTime ? new Date(booking.endDateTime).toLocaleTimeString() : '-' }}
                  </p>
                </div>
              </div>
            </div>

            <!-- Editable Fields -->
            <div class="mb-6">
              <h3 class="text-lg font-semibold mb-4">Edit Booking</h3>
              
              <div class="mb-4">
                <label for="facility" class="block text-sm font-medium mb-2">Facility</label>
                <InputText
                    id="facility"
                    v-model="updateValues.facilityName"
                    type="text"
                    disabled
                    class="w-full"
                />
              </div>

              <div class="mb-4">
                <label for="conduct" class="block text-sm font-medium mb-2">Conduct</label>
                <InputText
                    id="conduct"
                    v-model="updateValues.conduct"
                    type="text"
                    class="w-full"
                    placeholder="e.g., Meeting, Training"
                />
              </div>

              <div class="mb-4">
                <label for="description" class="block text-sm font-medium mb-2">Description</label>
                <Textarea
                    id="description"
                    v-model="updateValues.description"
                    :auto-resize="true"
                    rows="4"
                    class="w-full"
                    placeholder="Additional details..."
                />
              </div>

              <div class="mb-4">
                <label for="pocName" class="block text-sm font-medium mb-2">POC Name</label>
                <InputText
                    id="pocName"
                    v-model="updateValues.pocName"
                    type="text"
                    class="w-full"
                    placeholder="Point of Contact name"
                />
              </div>

              <div class="mb-4">
                <label for="pocPhone" class="block text-sm font-medium mb-2">POC Phone</label>
                <AutoComplete
                    id="pocPhone"
                    v-model="updateValues.pocPhone"
                    :suggestions="filteredItemsName"
                    :loading="nominalRollIsPending"
                    @complete="searchItems"
                    @item-select="optionSelect"
                    class="w-full"
                    placeholder="Search or enter POC phone"
                    option-label="name"
                />
              </div>
            </div>

            <!-- Actions -->
            <div class="flex gap-2 justify-end">
              <Button
                  type="button"
                  label="Cancel"
                  severity="secondary"
                  @click="goBack"
              />
              <Button
                  type="button"
                  label="Save"
                  :loading="updateIsPending"
                  @click="updateBooking"
              />
            </div>
          </template>
        </Card>

        <div v-else class="text-center py-8">
          <Skeleton height="2rem" class="mb-4" />
          <Skeleton height="10rem" />
        </div>
      </div>
    </div>
  </div>
</template>
