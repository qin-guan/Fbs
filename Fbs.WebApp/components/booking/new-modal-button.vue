<script setup lang="ts">
import { today, getLocalTimeZone } from '@internationalized/date'
import { useMutation, useQuery } from '@urql/vue'

const { df, tf } = useFormatter()

const todayZoned = today(getLocalTimeZone())

const modalOpen = ref(false)
const stepper = ref(0)
const formFacilityName = ref('')
const formStartDate = shallowRef(todayZoned)
const formConduct = ref('')
const formPocName = ref('')
const formPocPhone = ref('')
const formDescription = ref('')
const formInsertingBooking = ref(false)
const formInsertAnother = ref(false)

const slotSelection = reactive({
  selectionStart: -1,
  selectionEnd: -1,
  hovered: -1,
})

defineShortcuts({
  n: () => modalOpen.value = true,
})

const facilities = useQuery({
  query: queries.facilities,
  variables: {},
  pause: true,
})

const me = useQuery({
  query: queries.meWithName,
  variables: {},
})

whenever(me.data, (value) => {
  formPocName.value = value.me?.name ?? ''
  formPocPhone.value = value.me?.phone ?? ''
}, { immediate: true })

const slotsVariables = computed(() => ({
  startDate: formStartDate.value.toDate(getLocalTimeZone()).toISOString(),
  endDate: formStartDate.value.add({ days: 1 }).toDate(getLocalTimeZone()).toISOString(),
  facility: formFacilityName.value,
}))

const slots = useQuery({
  query: queries.slots,
  variables: slotsVariables,
  pause: computed(() => !formFacilityName.value),
})

const allSlots = computed(() => {
  return slots.data.value?.facilities.at(0)?.availableTimeSlots
})

const resolvedSlotSelection = computed(() => {
  if (slotSelection.selectionStart === -1 || slotSelection.selectionEnd === -1) {
    return {}
  }

  const start = new Date(allSlots.value?.slice(slotSelection.selectionStart, slotSelection.selectionEnd + 1).at(0)?.startDateTime)
  const end = new Date(allSlots.value?.slice(slotSelection.selectionStart, slotSelection.selectionEnd + 1).at(-1)?.endDateTime)

  return {
    start,
    end,
    dateRange: df.formatRange(start, end),
    range: tf.formatRange(start, end),
    duration: (end.getTime() - start.getTime()) / 60000,
  }
})

function modalOpened(open: boolean) {
  if (open) {
    facilities.resume()
  }
}

const slotWithMeta = computed(() => {
  // Handle cases where necessary reactive data isn't ready
  if (!allSlots.value || !slotSelection) {
    return []
  }

  const slots = allSlots.value
  const { selectionStart, selectionEnd, hovered } = slotSelection

  // Determine the range currently being hovered/selected (if interaction has started)
  let hoverRangeStart = -1
  let hoverRangeEnd = -1
  const isSelecting = selectionStart > -1 && selectionEnd === -1 && hovered > -1

  if (isSelecting) {
    // Determine the start and end of the potential selection range
    hoverRangeStart = Math.min(selectionStart, hovered)
    hoverRangeEnd = Math.max(selectionStart, hovered)
  }

  // Find the index of the first booked slot within the potential selection range
  let firstIntersectionIdx = -1
  if (isSelecting) {
    for (let i = hoverRangeStart; i <= hoverRangeEnd; i++) {
      // Check bounds and if the slot at index `i` exists and has a booking
      if (i >= 0 && i < slots.length && slots[i]?.booking) {
        // Store the index relative to the direction of selection
        // If selecting forwards (hover > start), block starts at the booked slot
        // If selecting backwards (hover < start), block starts *after* the booked slot index
        firstIntersectionIdx = i
        break // Found the first one
      }
    }
  }

  return slots.map((slot, idx) => {
    const ret = { slot, meta: { idx, state: 'available' } }

    // 1. Highest precedence: Unavailable (booked)
    if (slot.booking) {
      ret.meta.state = 'unavailable'
      return ret
    }

    if (new Date(slot.startDateTime) < new Date()) {
      ret.meta.state = 'pastBookingDate'
      return ret
    }

    // 2. Next precedence: Selected (finalized selection)
    // Ensure selectionEnd is valid and represents a completed selection
    if (selectionStart > -1 && selectionEnd > -1 && idx >= selectionStart && idx <= selectionEnd) {
      // Make sure the finalized selection doesn't contain unavailable slots
      // (This assumes selection logic prevents selecting over booked slots,
      // otherwise, additional checks might be needed here)
      ret.meta.state = 'selected'
      return ret
    }

    // 3. Next precedence: Hovered (during active selection process)
    if (isSelecting && idx >= hoverRangeStart && idx <= hoverRangeEnd) {
      // Check if the current slot is before the first intersection (if one exists)
      let isBeforeIntersection = true
      if (firstIntersectionIdx !== -1) {
        // If selecting forwards, hovered state applies up to (not including) the intersection
        // If selecting backwards, hovered state applies down to (not including) the intersection
        if (hovered >= selectionStart) { // Selecting forwards or hovering on start point
          isBeforeIntersection = idx < firstIntersectionIdx
        }
        else { // Selecting backwards
          isBeforeIntersection = idx > firstIntersectionIdx
        }
      }

      if (isBeforeIntersection) {
        ret.meta.state = 'hovered'
        return ret
      }
      else {
        // If we are at or past the intersection, default back to available (unless it's booked - handled above)
        // No explicit state change needed here, relies on default 'available' or prior 'unavailable'
        return ret // Keep state determined so far (likely 'available')
      }
    }

    // 4. Default: Available (if none of the above conditions met)
    return ret
  })
})

function onClick(idx: number) {
  const allSlotsMeta = slotWithMeta.value // Use the computed value which has booking info readily available

  // Case 1: A selection is already finalized. Start a new selection.
  if (slotSelection.selectionStart > -1 && slotSelection.selectionEnd > -1) {
    console.log(`Resetting selection. Starting new one at index ${idx}.`)
    slotSelection.selectionStart = idx
    slotSelection.selectionEnd = -1 // Reset end to indicate selection in progress
  }

  // Case 2: A selection has started, and this click might finalize it.
  else if (slotSelection.selectionStart > -1 && slotSelection.selectionEnd === -1) {
    const startIndex = slotSelection.selectionStart
    const endIndex = idx // The potential end of the selection

    // Determine the actual range to check for intersections
    const rangeStart = Math.min(startIndex, endIndex)
    const rangeEnd = Math.max(startIndex, endIndex)

    let hasIntersection = false
    for (let i = rangeStart; i <= rangeEnd; i++) {
      // Check bounds just in case, though idx should be valid if coming from UI
      if ((i >= 0 && i < allSlotsMeta.length && allSlotsMeta[i]?.slot?.booking) || allSlotsMeta[i]?.meta.state === 'pastBookingDate') {
        // Found a booked slot within the potential selection range
        hasIntersection = true
        console.warn(`Selection attempt blocked: Intersection detected at index ${i} within range [${rangeStart}, ${rangeEnd}].`)
        break // No need to check further
      }
    }

    // If no intersection is found, finalize the selection
    if (!hasIntersection) {
      console.log(`Finalizing selection: [${rangeStart}, ${rangeEnd}]`)
      // Ensure start is always the lower index and end is the higher index
      if (startIndex > endIndex) {
        slotSelection.selectionEnd = startIndex
        slotSelection.selectionStart = endIndex
      }
      else {
        // Start index remains the same
        slotSelection.selectionEnd = endIndex
      }
    }
    else {
      // Intersection detected: Abort finalizing the selection. Reset the process.
      // Start a new selection at the current click index (might be more intuitive)
      console.log(`Intersection found. Starting new selection at index ${idx}.`)
      slotSelection.selectionStart = idx
      slotSelection.selectionEnd = -1
    }
  }

  // Case 3: No selection active. Start a new selection.
  else if (slotSelection.selectionStart === -1 && slotSelection.selectionEnd === -1) {
    console.log(`Starting selection at index ${idx}.`)
    slotSelection.selectionStart = idx
    slotSelection.selectionEnd = -1
  }
}

const insertBooking = useMutation(mutations.insertBooking)
const toast = useToast()

function clickInsertBooking() {
  formInsertingBooking.value = true
  insertBooking.executeMutation({
    conduct: formConduct.value,
    description: formDescription.value,
    facilityName: formFacilityName.value,
    startDateTime: resolvedSlotSelection.value.start?.toISOString(),
    endDateTime: resolvedSlotSelection.value.end?.toISOString(),
    pocName: formPocName.value,
    pocPhone: formPocPhone.value,
  })
    .then(({ error }) => {
      if (!error) {
        if (!formInsertAnother.value) {
          modalOpen.value = false
        }

        stepper.value = 0
        formFacilityName.value = ''
        formStartDate.value = todayZoned
        formDescription.value = ''
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
      formInsertingBooking.value = false
    })
}
</script>

<template>
  <UModal
    v-model:open="modalOpen"
    :ui="{ content: 'max-w-3xl' }"
    :title="stepper === 2 ? 'Confirm your booking' : 'New booking'"
    @update:open="modalOpened"
  >
    <UButton label="New" />

    <template #body>
      <form
        v-if="stepper === 0"
        class="space-y-4"
        @submit.prevent="stepper++"
      >
        <UFormField
          label="Facility"
          name="facility"
        >
          <USelectMenu
            v-model="formFacilityName"
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
          label="Start Date"
          name="start-date"
        >
          <UPopover>
            <UButton
              color="neutral"
              variant="subtle"
              icon="i-lucide-calendar"
            >
              {{ formStartDate ? df.format(formStartDate.toDate(getLocalTimeZone())) : 'Select a date' }}
            </UButton>

            <template #content>
              <UCalendar
                v-model="formStartDate"
                :min-value="todayZoned"
                class="p-2"
              />
            </template>
          </UPopover>
        </UFormField>
      </form>

      <div
        v-if="stepper === 1"
        class="space-y-4"
      >
        <div class="flex justify-between items-center">
          <div>
            <span>
              Availability for:
            </span>

            <span class="font-semibold">
              {{ formFacilityName }}
            </span>
          </div>

          <span class="text-sm">
            Click on
            <UChip
              standalone
              inset
              color="error"
              size="3xl"
              text="Unavailable"
            /> slots for more info
          </span>
        </div>

        <div class="flex justify-between items-center">
          <div class="flex items-center gap-2">
            <div class="w-4 h-4 rounded-sm bg-green-50 border-1 border-green-400" />
            <span>
              Available
            </span>
          </div>

          <div class="flex items-center gap-2">
            <div class="w-4 h-4 rounded-sm bg-blue-50 border-1 border-blue-400" />
            <span>
              Selected
            </span>
          </div>

          <div class="flex items-center gap-2">
            <div class="w-4 h-4 rounded-sm bg-red-50 border-1 border-red-400" />
            <span>
              Booked
            </span>
          </div>

          <div class="flex items-center gap-2">
            <div class="w-4 h-4 rounded-sm bg-gray-50 border-1 border-gray-400" />
            <span>
              Unavailable
            </span>
          </div>
        </div>

        <div class="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-2">
          <template
            v-for="{ slot, meta } in slotWithMeta"
            :key="slot.startDateTime"
          >
            <BookingNewNewModalButtonTimeslotUnavailable
              v-if="slot.booking"
              :slot="slot"
              :time-slot="slot.startDateTime"
              :tooltip="slot.booking.user?.unit ?? 'N/A'"
            />
            <BookingNewNewModalButtonTimeslotAvailable
              v-else
              :state="meta.state"
              :time-slot="slot.startDateTime"
              @click="onClick(meta.idx)"
              @mouseover="slotSelection.hovered = meta.idx"
            />
          </template>
        </div>

        <UCard
          v-if="resolvedSlotSelection.range"
          :ui="{ body: 'p-3 sm:p-3 flex items-center justify-between' }"
        >
          <div>
            <span class="font-semibold">Booking details</span>
            <br>
            <span>
              {{ resolvedSlotSelection.range }}
            </span>
          </div>

          <UButton
            variant="soft"
            @click="slotSelection.selectionStart = -1; slotSelection.selectionEnd = -1"
          >
            Clear
          </UButton>
        </UCard>
      </div>

      <div
        v-if="stepper === 2"
        class="space-y-4"
      >
        <UCard
          class="bg-primary-100 border-1 border-primary-400"
          :ui="{ header: 'border-primary-200' }"
        >
          <template #header>
            <span class="font-semibold text-lg">
              Review booking details
            </span>
          </template>

          <div class="space-y-4">
            <div class="flex gap-3">
              <UAvatar
                icon="i-lucide-home"
                size="lg"
                class="bg-primary-200 mt-1"
              />
              <span>
                <span class="font-semibold text-lg">Facility</span>
                <br>
                <span>
                  {{ slotsVariables.facility }}
                </span>
              </span>
            </div>

            <div class="flex gap-3 items-center">
              <UAvatar
                icon="i-lucide-calendar"
                size="lg"
                class="bg-primary-200 mt-1"
              />
              <span>
                <span class="font-semibold text-lg">Date & Time</span>
                <br>
                <span>
                  {{ resolvedSlotSelection.dateRange }}, {{ resolvedSlotSelection.range }}
                </span>
              </span>
            </div>

            <div class="flex gap-3 items-center">
              <UAvatar
                icon="i-lucide-clock"
                size="lg"
                class="bg-primary-200 mt-1 "
              />
              <span>
                <span class="font-semibold text-lg">Duration</span>
                <br>
                <span>
                  {{ resolvedSlotSelection.duration }} minutes
                </span>
              </span>
            </div>
          </div>
        </UCard>

        <hr>

        <UFormField
          class="w-full"
          label="Conduct name"
          name="conduct"
          required
        >
          <UInput
            v-model="formConduct"
            autofocus
          />
        </UFormField>

        <div class="flex">
          <UFormField
            class="w-full"
            label="PoC Name"
            name="pocName"
            required
          >
            <UInput v-model="formPocName" />
          </UFormField>

          <UFormField
            class="w-full"
            label="PoC Phone"
            name="pocPhone"
            required
          >
            <UInput v-model="formPocPhone" />
          </UFormField>
        </div>

        <UFormField
          label="Description"
          name="description"
        >
          <UTextarea
            v-model="formDescription"
            autoresize
          />
        </UFormField>
      </div>
    </template>

    <template #footer>
      <UButton
        v-if="stepper > 0"
        variant="ghost"
        @click="stepper--"
      >
        Back
      </UButton>

      <UButton
        v-if="stepper === 0"
        class="ml-auto"
        :disabled="!formFacilityName"
        @click="stepper++"
      >
        Next
      </UButton>

      <UButton
        v-if="stepper === 1"
        class="ml-auto"
        :disabled="slotSelection.selectionEnd === -1 || slotSelection.selectionStart === -1"
        @click="stepper++"
      >
        Next
      </UButton>

      <div
        v-if="stepper === 2"
        class="flex items-center gap-4 ml-auto"
      >
        <UCheckbox
          v-model="formInsertAnother"
          label="Create another"
        />
        <UButton
          :loading="formInsertingBooking"
          @click="clickInsertBooking()"
        >
          Confirm
        </UButton>
      </div>
    </template>
  </UModal>
</template>
