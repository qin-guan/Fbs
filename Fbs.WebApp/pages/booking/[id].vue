<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { FastEndpointsProblemDetails, FbsWebApiDtosBookingWithUser } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const { data: me } = useMe()
const toast = useToast()
const { df, tf } = useFormatter()
const { data: booking, isPending: bookingIsPending } = useBooking(useRoute().params.id as string)

const { mutate: deleteMutate, isPending: deleteIsPending } = useDeleteBookingMutation(useRoute().params.id as string)
const { mutate: updateMutate, isPending: updateIsPending } = useUpdateBookingMutation(useRoute().params.id as string)

const updateValues = ref<FbsWebApiDtosBookingWithUser>({})
const deleteConfirmationDialog = ref(false)

const saveDisabled = computed(() => {
  if (!booking.value || !me.value) return true
  return booking.value.user?.phone !== me.value.phone
})

const menuItems: DropdownMenuItem[] = [
  {
    label: 'Delete',
    icon: 'i-lucide-trash-2',
    color: 'error',
    onSelect() {
      deleteConfirmationDialog.value = true
    },
  },
]

const userLink = computed(() => {
  if (!booking.value?.user?.phone) return ''
  return `https://api.whatsapp.com/send?phone=${booking.value?.user?.phone}`
})

const pocLink = computed(() => {
  if (!booking.value?.pocPhone) return ''
  return `https://api.whatsapp.com/send?phone=${booking.value?.pocPhone}`
})

// POC phones are stored with the 65 country code; the input only takes the local number.
function toLocalPhone(phone?: string | null) {
  return phone && /^65\d{8}$/.test(phone) ? phone.slice(2) : phone
}

whenever(booking, (newBooking) => {
  updateValues.value = { ...newBooking, pocPhone: toLocalPhone(newBooking.pocPhone) }
}, { immediate: true })

function optionSelect({ phone }: { phone: string }) {
  updateValues.value.pocPhone = phone.slice(2)
}

function showError(summary: string, error: unknown) {
  const e = error as FastEndpointsProblemDetails
  toast.add({
    title: summary,
    description: e?.errors?.find(a => a)?.reason ?? e?.title ?? undefined,
    color: 'error',
    icon: 'i-lucide-circle-x',
    duration: 3000,
  })
}

function deleteBooking() {
  deleteMutate({}, {
    async onSuccess() {
      deleteConfirmationDialog.value = false
      await navigateTo('/booking')
    },
    onError(error) {
      showError('Failed to delete booking.', error)
    },
  })
}

function updateBooking() {
  updateMutate({ ...updateValues.value, pocPhone: '65' + updateValues.value.pocPhone }, {
    async onSuccess() {
      toast.add({
        title: 'Booking updated successfully.',
        color: 'success',
        icon: 'i-lucide-circle-check',
        duration: 3000,
      })
    },
    onError(error) {
      showError('Failed to update booking.', error)
    },
  })
}
</script>

<template>
  <UDashboardPanel id="booking">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: '/booking' },
              { label: booking?.facilityName ?? 'Loading...', to: `/booking/${booking?.id}` },
            ]"
          />
        </template>

        <template #right>
          <UDropdownMenu
            :items="menuItems"
            :content="{ align: 'end' }"
          >
            <UButton
              color="neutral"
              variant="ghost"
              icon="i-lucide-ellipsis-vertical"
              aria-label="More actions"
            />
          </UDropdownMenu>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <UModal
        v-model:open="deleteConfirmationDialog"
        title="Delete booking"
        description="Are you sure you want to delete this booking?"
      >
        <template #body>
          <UAlert
            color="error"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            title="This is permanent and most definitely will result in pain and suffering if unintended!"
          />
        </template>

        <template #footer>
          <div class="flex w-full justify-end gap-2">
            <UButton
              type="button"
              label="Cancel"
              color="neutral"
              variant="outline"
              @click="deleteConfirmationDialog = false"
            />
            <UButton
              type="button"
              label="Delete"
              color="error"
              icon="i-lucide-trash-2"
              :loading="deleteIsPending"
              @click="deleteBooking"
            />
          </div>
        </template>
      </UModal>

      <div class="w-full lg:max-w-3xl mx-auto flex flex-col gap-6">
        <div
          v-if="bookingIsPending"
          class="space-y-3"
        >
          <USkeleton class="h-5 w-32" />
          <USkeleton class="h-8 w-2/3" />
          <USkeleton class="h-80 w-full" />
        </div>

        <template v-else>
          <div class="flex flex-col gap-2">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <UBadge
                :label="booking?.facilityName ?? undefined"
                icon="i-lucide-map-pin"
                color="neutral"
                variant="subtle"
              />

              <span class="text-sm text-muted">
                Created by
                <ULink
                  :to="userLink"
                  target="_blank"
                  class="font-medium text-default hover:underline"
                >
                  {{ booking?.user?.name }}
                </ULink>
              </span>
            </div>

            <h2 class="text-2xl font-semibold text-highlighted break-words">
              {{ updateValues.conduct || 'Untitled conduct' }}
            </h2>

            <p
              v-if="booking?.startDateTime && booking?.endDateTime"
              class="flex items-center gap-1.5 text-sm text-muted"
            >
              <UIcon
                name="i-lucide-clock"
                class="size-4"
              />
              {{ df.format(booking.startDateTime) }}, {{ tf.format(booking.startDateTime) }} – {{ tf.format(booking.endDateTime) }}
            </p>
          </div>

          <form
            class="flex flex-col gap-6"
            @submit.prevent="updateBooking"
          >
            <UPageCard variant="subtle">
              <div class="grid gap-5 sm:grid-cols-2">
                <UFormField
                  label="Conduct"
                  class="sm:col-span-2"
                >
                  <UInput
                    id="conduct"
                    v-model="updateValues.conduct"
                    class="w-full"
                  />
                </UFormField>

                <UFormField label="POC Rank and Name">
                  <PocNameInput
                    id="pocName"
                    v-model="updateValues.pocName"
                    @select="optionSelect"
                  />
                </UFormField>

                <UFormField label="POC Phone">
                  <UInput
                    id="pocPhone"
                    v-model="updateValues.pocPhone"
                    type="tel"
                    inputmode="numeric"
                    class="w-full"
                    :ui="{ base: 'ps-12', leading: 'pointer-events-none' }"
                  >
                    <template #leading>
                      <span class="text-sm text-muted">+65</span>
                    </template>
                  </UInput>
                </UFormField>

                <UFormField
                  label="Description"
                  class="sm:col-span-2"
                >
                  <UTextarea
                    id="description"
                    v-model="updateValues.description"
                    :rows="3"
                    autoresize
                    class="w-full"
                  />
                </UFormField>

                <UFormField
                  label="Start"
                  help="Create a new booking if you wish to change the date"
                >
                  <UInput
                    id="start"
                    :model-value="booking?.startDateTime ? `${df.format(booking.startDateTime)}, ${tf.format(booking.startDateTime)}` : ''"
                    icon="i-lucide-calendar"
                    disabled
                    class="w-full"
                  />
                </UFormField>

                <UFormField label="End">
                  <UInput
                    id="end"
                    :model-value="booking?.endDateTime ? `${df.format(booking.endDateTime)}, ${tf.format(booking.endDateTime)}` : ''"
                    icon="i-lucide-calendar"
                    disabled
                    class="w-full"
                  />
                </UFormField>
              </div>
            </UPageCard>

            <div class="flex flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-between">
              <UButton
                :to="pocLink"
                target="_blank"
                label="Contact POC"
                icon="i-simple-icons-whatsapp"
                color="neutral"
                variant="outline"
                class="justify-center"
              />

              <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
                <p
                  v-if="saveDisabled && booking && me"
                  class="text-xs text-muted"
                >
                  Only the person who made this booking can save changes.
                </p>
                <UButton
                  label="Save"
                  type="submit"
                  icon="i-lucide-save"
                  :loading="updateIsPending"
                  :disabled="saveDisabled"
                  class="justify-center"
                />
              </div>
            </div>
          </form>
        </template>
      </div>
    </template>
  </UDashboardPanel>
</template>
