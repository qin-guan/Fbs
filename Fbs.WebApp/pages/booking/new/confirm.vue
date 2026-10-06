<script setup lang="ts">
import type { FormError, FormErrorEvent, FormSubmitEvent } from '@nuxt/ui'
import { useGetAuthMe, useGetBooking, type FbsWebApiEntitiesBooking } from '~/api'
import type { BookingSlot } from '~/composables/booking-slots'

definePageMeta({
  layout: 'app',
})

const { $driver } = useNuxtApp()
const onboarded = useLocalStorage<boolean>('new-confirm-onboarded', false)

const router = useRouter()
const { df, tf } = useFormatter()
const basket = useBookingBasket()
const { data: me } = useGetAuthMe()
const { data: bookings } = useGetBooking()
const { mutate: createMutate, isPending: createIsPending } = useCreateBookingBatchMutation()
const { remember: rememberCustomPoc } = useCustomPocs()

const route = useRoute()
const toast = useToast()

onMounted(() => {
  if (!onboarded.value) {
    $driver.setConfig({
      showProgress: true,
      steps: [
        { element: '#poc-rank-and-name', popover: { title: 'Autocomplete', description: 'Choose from the list of existing POCs, or enter your own!' } },
        {
          element: '#crumbs', popover: {
            title: 'Going back', description: 'Click to go back to the previous page!', onNextClick() {
              onboarded.value = true
              $driver.moveNext()
            },
          },
        },
      ],
      onCloseClick() {
        onboarded.value = true
      },
    })
    $driver.drive()
  }
})

// A single slot arrives in the URL; several come from the booking list
const querySlot = computed<BookingSlot | undefined>(() => {
  const query = route.query
  if (!query['start-date'] || !query['end-date'] || !query['facility-name']) return undefined

  return {
    id: 'query',
    facilityName: query['facility-name'] as string,
    start: new Date(query['start-date'] as string),
    end: new Date(query['end-date'] as string),
  }
})
const fromList = computed(() => !querySlot.value)
const slots = computed(() => querySlot.value ? [querySlot.value] : basket.slots.value)
const originalQuery = computed(() => (route.query['original-query'] as string | undefined) ?? '')

const clashes = computed(() => findClashes(slots.value, bookings.value))
const serverErrors = ref<Record<number, string>>({})
const problemCount = computed(() => slots.value.filter((_, i) => clashes.value[i] || serverErrors.value[i]).length)
const created = ref<FbsWebApiEntitiesBooking[]>([])
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

// Default the POC to the signed-in user, without overwriting anything already entered.
// User phones are stored with the 65 country code; the input only takes the local number.
whenever(me, (user) => {
  if (!state.pocName && user.name) {
    state.pocName = user.name
  }
  if (!state.pocPhone && user.phone && /^65\d{8}$/.test(user.phone)) {
    state.pocPhone = user.phone.slice(2)
  }
}, { immediate: true, once: true })

function optionSelect({ phone }: { phone: string }) {
  state.pocPhone = phone.slice(2)
}

function removeSlot(index: number) {
  const slot = slots.value[index]
  if (slot && fromList.value) basket.remove([slot.id])
}

function removeProblemSlots() {
  basket.remove(slots.value.filter((_, i) => clashes.value[i] || serverErrors.value[i]).map(s => s.id))
}

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []

  if (!values.conduct) {
    errors.push({ name: 'conduct', message: 'Conduct name is required.' })
  }

  if (!values.pocName) {
    errors.push({ name: 'pocName', message: 'A point of contact is needed.' })
  }

  if (!values.pocPhone) {
    errors.push({ name: 'pocPhone', message: 'Their phone number is needed.' })
  }
  else if (values.pocPhone.length !== 8) {
    errors.push({ name: 'pocPhone', message: 'Use 8 digits.' })
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

function onFormSubmit({ data }: FormSubmitEvent<typeof state>) {
  const submitted = slots.value
  if (!submitted.length || problemCount.value) return

  if (errorToastId !== undefined) {
    toast.remove(errorToastId)
    errorToastId = undefined
  }

  createMutate({
    body: {
      conduct: data.conduct,
      pocName: data.pocName,
      pocPhone: '65' + data.pocPhone,
      description: data.description,
      slots: submitted.map(slot => ({
        facilityName: slot.facilityName,
        startDateTime: slot.start,
        endDateTime: slot.end,
      })),
    },
  }, {
    onError(error) {
      const e = getProblemDetails(error)
      const slotErrors: Record<number, string> = {}
      const otherErrors: string[] = []
      for (const err of e?.errors ?? []) {
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
          : Object.values(slotErrors)[0] ?? otherErrors[0],
        color: 'error',
        icon: 'i-lucide-circle-x',
      }).id
    },
    async onSuccess(result) {
      rememberCustomPoc({ name: data.pocName, phone: '65' + data.pocPhone })

      if (fromList.value) {
        basket.remove(submitted.map(s => s.id))
      }

      const first = result?.[0]
      if (result?.length === 1 && first?.id) {
        toast.add({
          title: 'Booking created',
          description: `${first.facilityName} is booked.`,
          color: 'success',
          icon: 'i-lucide-circle-check',
        })
        await router.push(`/booking/${first.id}`)
        return
      }

      created.value = result ?? []
      toast.add({
        title: `${created.value.length} bookings created`,
        color: 'success',
        icon: 'i-lucide-circle-check',
      })
    },
  })
}
</script>

<template>
  <UDashboardPanel id="booking-confirm">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: '/booking' },
              { label: 'New', to: `/booking/new${originalQuery}`, slot: 'crumbs' },
              { label: querySlot?.facilityName ?? (created.length ? 'Done' : `${slots.length} ${slots.length === 1 ? 'slot' : 'slots'}`) },
            ]"
          >
            <template #crumbs-label="{ item }">
              <span id="crumbs">{{ item.label }}</span>
            </template>
          </UBreadcrumb>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="w-full lg:max-w-3xl mx-auto">
        <UPageCard
          v-if="created.length"
          :title="`${created.length} bookings created`"
          description="A Telegram message is sent for each booking, as usual."
          icon="i-lucide-circle-check"
          variant="subtle"
          :ui="{ leadingIcon: 'text-success' }"
        >
          <ul class="divide-y divide-default">
            <li
              v-for="booking in created"
              :key="booking.id ?? undefined"
            >
              <ULink
                :to="`/booking/${booking.id}`"
                class="flex items-center justify-between gap-3 py-2 hover:text-highlighted"
              >
                <span class="min-w-0">
                  <span class="block truncate text-sm font-medium text-highlighted">{{ booking.facilityName }}</span>
                  <span
                    v-if="booking.startDateTime && booking.endDateTime"
                    class="block truncate text-xs text-muted"
                  >
                    {{ describeSlot({ start: booking.startDateTime, end: booking.endDateTime }, df, tf).date }}
                    · {{ describeSlot({ start: booking.startDateTime, end: booking.endDateTime }, df, tf).time }}
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
              to="/booking"
              label="View all bookings"
              color="neutral"
              variant="outline"
              class="justify-center"
            />
            <UButton
              :to="`/booking/new${originalQuery}`"
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
          description="Pick a facility and time slot on the timeline first."
          :actions="[{ label: 'Back to new booking', to: `/booking/new${originalQuery}`, color: 'error', variant: 'outline' }]"
        />

        <UForm
          v-else
          :state="state"
          :validate="validate"
          :validate-on="['input']"
          class="flex flex-col gap-6"
          @submit="onFormSubmit"
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
                v-if="fromList"
                :to="`/booking/new${originalQuery}`"
                label="Add more"
                icon="i-lucide-plus"
                color="neutral"
                variant="ghost"
                size="xs"
              />
            </div>

            <BookingSlotList
              :slots="slots"
              :clashes="clashes"
              :errors="serverErrors"
              :removable="fromList"
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
                ? 'Go back and pick another time.'
                : 'Bookings are made all at once, so remove the slots that clash to continue.'"
              :actions="fromList
                ? [{ label: problemCount === 1 ? 'Remove it' : 'Remove them', color: 'error', variant: 'outline', onClick: removeProblemSlots }]
                : [{ label: 'Pick another time', to: `/booking/new${originalQuery}`, color: 'error', variant: 'outline' }]"
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
                id="poc-rank-and-name"
                label="POC Rank and Name"
                name="pocName"
              >
                <PocNameInput
                  v-model="state.pocName"
                  placeholder="Search the nominal roll"
                  @select="optionSelect"
                />
              </UFormField>

              <UFormField
                label="POC Phone"
                name="pocPhone"
              >
                <UInput
                  v-model="state.pocPhone"
                  type="tel"
                  inputmode="numeric"
                  placeholder="8888 9999"
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

          <UAlert
            color="primary"
            variant="subtle"
            icon="i-lucide-heart-handshake"
            title="Be gracious to others!"
          >
            <template #description>
              <p>Book only what you need, and leave the facility in a better condition than you found it!</p>
              <p>Thank you :3</p>
            </template>
          </UAlert>

          <UButton
            :loading="createIsPending"
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
