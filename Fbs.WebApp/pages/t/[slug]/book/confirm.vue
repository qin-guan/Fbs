<script setup lang="ts">
import type { FormError, FormErrorEvent } from '@nuxt/ui'
import { usePostOrgBookings, useGetOrgBookings, useGetOrgFacilitiesBookable, type FbsWebApiEndpointsOrgBookingsBookingResponse } from '~/api'
import { describeSlot, findClashes, type KnownBooking } from '~/lib/slots'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Confirm booking' })

const router = useRouter()
const toast = useToast()
const invalidateBookings = useInvalidateBookings()
const { slug, org, timeZone, path } = useTenant()
const { df, tf } = useTenantFormatter()
const basket = useTenantBasket()
const now = useNow({ interval: 60_000 })

const { data: facilities } = useGetOrgFacilitiesBookable({ path: computed(() => ({ slug: slug.value })) })
const facilityNames = computed(() => facilities.value?.map(f => ({ id: f.id, name: f.name })) ?? [])
const slots = computed(() => basket.slots.value)

// What the slots would clash with is read for their days, when that is a span that can be asked for
const span = computed(() => {
  if (!slots.value.length) {
    return undefined
  }

  return { from: new Date(Math.min(...slots.value.map(s => s.start.getTime()))), to: new Date(Math.max(...slots.value.map(s => s.end.getTime()))) }
})
const spanFits = computed(() => !!span.value && span.value.to.getTime() - span.value.from.getTime() <= 93 * 86_400_000)
const { data: bookings } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: span.value?.from, to: span.value?.to, mine: false })),
}, { query: { enabled: spanFits } })

const known = computed<KnownBooking[]>(() => (bookings.value ?? []).map(b => ({ id: b.id, facilityId: b.facilityId, startDateTime: b.startDateTime, endDateTime: b.endDateTime, conduct: b.conduct, bookedBy: b.bookedBy })))
const clashes = computed(() => findClashes(slots.value, known.value, now.value))
const serverErrors = ref<Record<number, string>>({})
const problemCount = computed(() => slots.value.filter((_, i) => clashes.value[i] || serverErrors.value[i]).length)
const created = ref<FbsWebApiEndpointsOrgBookingsBookingResponse[]>([])
let errorToastId: string | number | undefined

watch(slots, () => {
  serverErrors.value = {}
})

const state = reactive({
  conduct: '',
  pocName: '',
  pocPhone: '',
  description: '',
})

// Default the point of contact to the caller, without overwriting anything already entered
whenever(org, (value) => {
  if (!state.pocName) {
    state.pocName = value.me.displayName
  }
  if (!state.pocPhone && value.me.phone) {
    state.pocPhone = value.me.phone
  }
}, { immediate: true, once: true })

function removeSlot(index: number) {
  const slot = slots.value[index]
  if (slot) basket.remove([slot.id])
}

function removeProblemSlots() {
  basket.remove(slots.value.filter((_, i) => clashes.value[i] || serverErrors.value[i]).map(s => s.id))
}

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (!values.conduct.trim()) {
    errors.push({ name: 'conduct', message: 'Conduct name is required.' })
  }
  else if (values.conduct.length > 100) {
    errors.push({ name: 'conduct', message: 'Keep it to 100 characters.' })
  }

  if (!values.pocName.trim()) {
    errors.push({ name: 'pocName', message: 'A point of contact is needed.' })
  }

  if (!values.pocPhone.trim()) {
    errors.push({ name: 'pocPhone', message: 'Their phone number is needed.' })
  }

  return errors
}

function onFormError(event: FormErrorEvent) {
  const id = event.errors[0]?.id
  if (id) {
    // Inputs are disabled while the form submits; focus once they are re-enabled
    setTimeout(() => document.getElementById(id)?.focus())
  }
}

const { mutate: create, isPending: creating } = usePostOrgBookings()

function onSubmit() {
  const submitted = slots.value
  if (!submitted.length || problemCount.value) return

  if (errorToastId !== undefined) {
    toast.remove(errorToastId)
    errorToastId = undefined
  }

  create({
    path: { slug: slug.value },
    body: {
      conduct: state.conduct.trim(),
      pocName: state.pocName.trim(),
      pocPhone: state.pocPhone.trim(),
      description: state.description.trim() || null,
      slots: submitted.map(slot => ({ facilityId: slot.facilityId, startDateTime: slot.start, endDateTime: slot.end })),
    },
  }, {
    onError(error) {
      // A rejected batch usually means someone else booked first, so what was read is out of date
      invalidateBookings(slug.value)
      const problem = getProblemDetails(error)
      const slotErrors: Record<number, string> = {}
      const otherErrors: string[] = []
      for (const err of problem?.errors ?? []) {
        const match = /^slots\[(\d+)\]/i.exec(err.name ?? '')
        if (match) {
          slotErrors[Number(match[1])] ??= err.reason ?? 'This slot can\'t be booked'
        }
        else if (err.reason) {
          otherErrors.push(err.reason)
        }
      }
      serverErrors.value = slotErrors

      // The batch is all or nothing, so nothing was booked
      const count = Object.keys(slotErrors).length
      errorToastId = toast.add({
        title: submitted.length > 1 && count ? 'Nothing was booked' : 'Couldn\'t make the booking',
        description: submitted.length > 1 && count
          ? `${count} ${count === 1 ? 'slot' : 'slots'} can't be booked. Remove ${count === 1 ? 'it' : 'them'} and try again.`
          : Object.values(slotErrors)[0] ?? otherErrors[0] ?? (getErrorStatus(error) === 429 ? 'Wait a little, then try again.' : undefined),
        color: 'error',
        icon: 'i-lucide-circle-x',
      }).id
    },
    async onSuccess(result) {
      basket.remove(submitted.map(s => s.id))
      await invalidateBookings(slug.value)

      const first = result[0]
      if (result.length === 1 && first) {
        toast.add({ title: 'Booking created', description: `${first.facilityName} is booked.`, color: 'success', icon: 'i-lucide-circle-check' })
        await router.push(path('bookings', first.id))
        return
      }

      created.value = result
      toast.add({ title: `${result.length} bookings created`, color: 'success', icon: 'i-lucide-circle-check' })
    },
  })
}

const describe = (slot: { start: Date, end: Date }) => describeSlot(slot, df.value, tf.value, timeZone.value)
</script>

<template>
  <UDashboardPanel id="booking-confirm">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: path() },
              { label: 'New', to: path('book') },
              { label: created.length ? 'Done' : `${slots.length} ${slots.length === 1 ? 'slot' : 'slots'}` },
            ]"
          />
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="w-full lg:max-w-3xl mx-auto">
        <UPageCard
          v-if="created.length"
          :title="`${created.length} bookings created`"
          description="People who get booking notices receive one message for the whole batch."
          icon="i-lucide-circle-check"
          variant="subtle"
          :ui="{ leadingIcon: 'text-success' }"
        >
          <ul class="divide-y divide-default">
            <li
              v-for="booking in created"
              :key="booking.id"
            >
              <ULink
                :to="path('bookings', booking.id)"
                class="flex items-center justify-between gap-3 py-2 hover:text-highlighted"
              >
                <span class="min-w-0">
                  <span class="block truncate text-sm font-medium text-highlighted">{{ booking.facilityName }}</span>
                  <span class="block truncate text-xs text-muted">
                    {{ describe({ start: booking.startDateTime, end: booking.endDateTime }).date }}
                    · {{ describe({ start: booking.startDateTime, end: booking.endDateTime }).time }}
                  </span>
                </span>
                <UIcon
                  name="i-lucide-chevron-right"
                  class="size-4 shrink-0 text-dimmed"
                />
              </ULink>
            </li>
          </ul>

          <div class="flex flex-col gap-2 sm:flex-row">
            <UButton
              :to="path()"
              label="View all bookings"
              color="neutral"
              variant="outline"
              class="justify-center"
            />
            <UButton
              :to="path('book')"
              label="Make another booking"
              icon="i-lucide-calendar-plus"
              class="justify-center"
            />
          </div>
        </UPageCard>

        <UAlert
          v-else-if="!slots.length"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
          title="No time slot selected"
          description="Nothing's selected yet. Pick a facility and a time back on the timeline."
          :actions="[{ label: 'Back to new booking', to: path('book'), color: 'error', variant: 'outline' }]"
        />

        <UForm
          v-else
          :state="state"
          :validate="validate"
          :validate-on="['input']"
          class="flex flex-col gap-6"
          @submit="onSubmit"
          @error="onFormError"
        >
          <h2 class="text-lg font-semibold text-highlighted">
            {{ slots.length === 1 ? 'Confirm your booking' : `Confirm ${slots.length} bookings` }}
          </h2>

          <UPageCard
            variant="subtle"
            :ui="{ container: 'gap-y-3' }"
          >
            <div class="flex items-center justify-between gap-2">
              <h3 class="text-sm font-semibold text-highlighted">
                {{ slots.length === 1 ? 'Slot' : `${slots.length} slots` }}
              </h3>
              <UButton
                :to="path('book')"
                label="Add more"
                icon="i-lucide-plus"
                color="neutral"
                variant="ghost"
                size="xs"
              />
            </div>

            <TenantSlotList
              :slots="slots"
              :facilities="facilityNames"
              :clashes="clashes"
              :errors="serverErrors"
              removable
              class="max-h-80 overflow-y-auto"
              @remove="removeSlot"
            />

            <UAlert
              v-if="problemCount"
              color="error"
              variant="subtle"
              icon="i-lucide-circle-alert"
              :title="slots.length === 1
                ? 'This slot can\'t be booked.'
                : `${problemCount} ${problemCount === 1 ? 'slot' : 'slots'} can't be booked.`"
              :description="slots.length === 1
                ? 'Remove it and pick another time.'
                : 'They go in as one batch, so take out the clashes and try again.'"
              :actions="[{ label: problemCount === 1 ? 'Remove it' : 'Remove them', color: 'error', variant: 'outline', onClick: removeProblemSlots }]"
            />
          </UPageCard>

          <UPageCard variant="subtle">
            <div class="grid gap-5 sm:grid-cols-2">
              <UFormField
                label="Conduct"
                name="conduct"
                class="sm:col-span-2"
              >
                <UInput
                  v-model="state.conduct"
                  placeholder="Conduct name"
                  size="xl"
                  autofocus
                  class="w-full"
                />
              </UFormField>

              <UFormField
                label="Point of contact"
                name="pocName"
              >
                <UInput
                  v-model="state.pocName"
                  placeholder="Rank and name"
                  class="w-full"
                />
              </UFormField>

              <UFormField
                label="Their phone"
                name="pocPhone"
              >
                <UInput
                  v-model="state.pocPhone"
                  type="tel"
                  inputmode="tel"
                  placeholder="8888 9999"
                  class="w-full"
                />
              </UFormField>

              <UFormField
                label="Description"
                name="description"
                class="sm:col-span-2"
              >
                <UTextarea
                  v-model="state.description"
                  :rows="3"
                  autoresize
                  class="w-full"
                />
              </UFormField>
            </div>
          </UPageCard>

          <UButton
            :loading="creating"
            :label="slots.length === 1 ? 'Confirm' : `Confirm ${slots.length} bookings`"
            :disabled="!!problemCount"
            type="submit"
            size="lg"
            block
          />
        </UForm>
      </div>
    </template>
  </UDashboardPanel>
</template>
