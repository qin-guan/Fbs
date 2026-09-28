<script setup lang="ts">
import { CalendarDate, getLocalTimeZone, today } from '@internationalized/date'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useGetAuthMe, useGetBooking, useGetBookingById, type FbsWebApiDtosBookingWithUser } from '~/api'

definePageMeta({
  layout: 'app',
})

const id = useRoute().params.id as string

const { data: me } = useGetAuthMe()
const toast = useToast()
const { df, tf } = useFormatter()
const { data: booking, isPending: bookingIsPending } = useGetBookingById({ path: { id } })
const { data: bookings } = useGetBooking()

const { mutate: deleteMutate, isPending: deleteIsPending } = useDeleteBookingMutation()
const { mutate: updateMutate, isPending: updateIsPending } = useUpdateBookingMutation()

const updateValues = ref<FbsWebApiDtosBookingWithUser>({})
const deleteConfirmationDialog = ref(false)

const isOwnBooking = computed(() => !!booking.value && !!me.value && booking.value.user?.phone === me.value.phone)

// Anyone in the booker's unit can cancel the booking, and admins can cancel any booking
const canCancel = computed(() => {
  if (!booking.value || !me.value) return false
  if (isOwnBooking.value || me.value.isAdmin) return true
  return !!me.value.unit && me.value.unit === booking.value.user?.unit
})

const menuItems = computed<DropdownMenuItem[]>(() => [
  {
    label: 'Cancel booking',
    icon: 'i-lucide-calendar-x',
    color: 'error',
    disabled: !canCancel.value,
    onSelect() {
      deleteConfirmationDialog.value = true
    },
  },
])

// Time slot
const times = Array.from({ length: 48 }, (_, i) => {
  const hours = Math.floor(i / 2)
  const minutes = i % 2 ? 30 : 0
  return {
    value: `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`,
    label: tf.format(new Date(2000, 0, 1, hours, minutes)),
  }
})

// shallowRef keeps the DateValue class types intact for UCalendar
const startDay = shallowRef<CalendarDate>()
const endDay = shallowRef<CalendarDate>()
const startTime = ref('00:00')
const endTime = ref('00:00')

function toCalendarDate(date: Date) {
  return new CalendarDate(date.getFullYear(), date.getMonth() + 1, date.getDate())
}

function toTime(date: Date) {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

function toDateTime(day: CalendarDate | undefined, time: string) {
  if (!day) return undefined
  const [hours, minutes] = time.split(':').map(Number)
  const date = day.toDate(getLocalTimeZone())
  date.setHours(hours ?? 0, minutes ?? 0, 0, 0)
  return date
}

function resetTimeSlot() {
  const start = booking.value?.startDateTime
  const end = booking.value?.endDateTime
  if (!start || !end) return
  startDay.value = toCalendarDate(start)
  startTime.value = toTime(start)
  endDay.value = toCalendarDate(end)
  endTime.value = toTime(end)
}

const newStart = computed(() => toDateTime(startDay.value, startTime.value))
const newEnd = computed(() => toDateTime(endDay.value, endTime.value))

const timeChanged = computed(() => {
  const start = booking.value?.startDateTime
  const end = booking.value?.endDateTime
  if (!start || !end || !newStart.value || !newEnd.value) return false
  return newStart.value.getTime() !== start.getTime() || newEnd.value.getTime() !== end.getTime()
})

// Re-evaluated every minute so a booking that starts or ends while the page is open locks accordingly
const now = useNow({ interval: 60_000 })
const hasStarted = computed(() => !!booking.value?.startDateTime && booking.value.startDateTime <= now.value)
const isOver = computed(() => !!booking.value?.endDateTime && booking.value.endDateTime <= now.value)
const minDate = today(getLocalTimeZone())

const rescheduleProblem = computed(() => {
  const start = booking.value?.startDateTime
  const end = booking.value?.endDateTime
  if (!timeChanged.value || !start || !end || !newStart.value || !newEnd.value) return undefined
  return findRescheduleProblem(
    { id: booking.value?.id, facilityName: booking.value?.facilityName, start, end },
    { start: newStart.value, end: newEnd.value },
    bookings.value,
    now.value,
  )
})

const rescheduleError = computed(() => {
  const problem = rescheduleProblem.value
  switch (problem?.kind) {
    case 'over': return 'This booking is over, so its time can no longer be changed.'
    case 'started': return 'This booking has started, so only its end time can be changed.'
    case 'past': return 'The new time slot must be in the future.'
    case 'order': return 'End must be after the start.'
    case 'interval': return 'Times must be on the hour or half hour.'
    case 'existing': {
      const other = problem.booking
      const by = other.user?.unit ? ` (${other.user.unit})` : ''
      return `Clashes with "${other.conduct}"${by}, ${describeRange(other.startDateTime, other.endDateTime)}.`
    }
    default: return undefined
  }
})

function describeRange(start?: Date | null, end?: Date | null) {
  if (!start || !end) return ''
  const sameDay = toCalendarDate(start).compare(toCalendarDate(end)) === 0
  return sameDay
    ? `${df.format(start)}, ${tf.format(start)} – ${tf.format(end)}`
    : `${df.format(start)}, ${tf.format(start)} – ${df.format(end)}, ${tf.format(end)}`
}

const timeSlotHelp = computed(() => {
  if (!isOwnBooking.value) return undefined
  if (isOver.value) return 'This booking is over, so its time can no longer be changed.'
  if (hasStarted.value) return 'This booking has started, so only its end time can be changed.'
  return undefined
})

const saveDisabled = computed(() => !isOwnBooking.value || !!rescheduleError.value)

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
  resetTimeSlot()
}, { immediate: true })

function optionSelect({ phone }: { phone: string }) {
  updateValues.value.pocPhone = phone.slice(2)
}

function showError(summary: string, error: unknown) {
  const e = getProblemDetails(error)
  toast.add({
    title: summary,
    description: e?.errors?.find(a => a)?.reason ?? e?.title ?? undefined,
    color: 'error',
    icon: 'i-lucide-circle-x',
    duration: 3000,
  })
}

function deleteBooking() {
  deleteMutate({ path: { id } }, {
    async onSuccess() {
      deleteConfirmationDialog.value = false
      toast.add({
        title: 'Booking cancelled.',
        color: 'success',
        icon: 'i-lucide-circle-check',
        duration: 3000,
      })
      await navigateTo('/booking')
    },
    onError(error) {
      showError('Failed to cancel booking.', error)
    },
  })
}

function updateBooking() {
  updateMutate({
    path: { id },
    body: {
      conduct: updateValues.value.conduct ?? '',
      description: updateValues.value.description,
      pocName: updateValues.value.pocName,
      pocPhone: '65' + updateValues.value.pocPhone,
      // Leaving the times out keeps the current time slot
      startDateTime: timeChanged.value ? newStart.value : undefined,
      endDateTime: timeChanged.value ? newEnd.value : undefined,
    },
  }, {
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
        title="Cancel booking"
        description="Are you sure you want to cancel this booking?"
      >
        <template #body>
          <div class="space-y-3">
            <UAlert
              color="error"
              variant="subtle"
              icon="i-lucide-triangle-alert"
              title="This is permanent and most definitely will result in pain and suffering if unintended!"
            />
            <p
              v-if="!isOwnBooking && booking?.user?.name"
              class="text-sm text-muted"
            >
              This booking was made by <span class="font-medium text-default">{{ booking.user.name }}</span>.
              They will be notified on Telegram that you cancelled it.
            </p>
          </div>
        </template>

        <template #footer>
          <div class="flex w-full justify-end gap-2">
            <UButton
              type="button"
              label="Keep booking"
              color="neutral"
              variant="outline"
              @click="deleteConfirmationDialog = false"
            />
            <UButton
              type="button"
              label="Cancel booking"
              color="error"
              icon="i-lucide-calendar-x"
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
                  :help="timeSlotHelp"
                  :error="rescheduleError ? true : undefined"
                >
                  <div class="flex gap-2">
                    <UPopover>
                      <UButton
                        id="start"
                        color="neutral"
                        variant="outline"
                        icon="i-lucide-calendar"
                        :label="startDay ? df.format(startDay.toDate(getLocalTimeZone())) : 'Choose date'"
                        :disabled="!isOwnBooking || hasStarted"
                        class="flex-1"
                      />

                      <template #content="{ close }">
                        <div class="p-2">
                          <UCalendar
                            v-model="startDay"
                            :min-value="minDate"
                            @update:model-value="close"
                          />
                        </div>
                      </template>
                    </UPopover>

                    <USelect
                      v-model="startTime"
                      :items="times"
                      :disabled="!isOwnBooking || hasStarted"
                      aria-label="Start time"
                      class="w-32"
                    />
                  </div>
                </UFormField>

                <UFormField
                  label="End"
                  :error="rescheduleError"
                >
                  <div class="flex gap-2">
                    <UPopover>
                      <UButton
                        id="end"
                        color="neutral"
                        variant="outline"
                        icon="i-lucide-calendar"
                        :label="endDay ? df.format(endDay.toDate(getLocalTimeZone())) : 'Choose date'"
                        :disabled="!isOwnBooking || isOver"
                        class="flex-1"
                      />

                      <template #content="{ close }">
                        <div class="p-2">
                          <UCalendar
                            v-model="endDay"
                            :min-value="startDay ?? minDate"
                            @update:model-value="close"
                          />
                        </div>
                      </template>
                    </UPopover>

                    <USelect
                      v-model="endTime"
                      :items="times"
                      :disabled="!isOwnBooking || isOver"
                      aria-label="End time"
                      class="w-32"
                    />
                  </div>
                </UFormField>

                <div
                  v-if="timeChanged"
                  class="sm:col-span-2 flex items-center justify-between gap-2 text-xs text-muted"
                >
                  <span>Currently {{ describeRange(booking?.startDateTime, booking?.endDateTime) }}</span>
                  <UButton
                    label="Undo time change"
                    color="neutral"
                    variant="link"
                    size="xs"
                    @click="resetTimeSlot"
                  />
                </div>
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
                  v-if="!isOwnBooking && booking && me"
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
