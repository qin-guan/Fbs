<script setup lang="ts">
import { VueCal } from 'vue-cal'
import 'vue-cal/style'
import { useGetBooking, useGetFacility } from '~/api'
import type { NewBookingSlot, SlotClash } from '~/composables/booking-slots'

definePageMeta({
  layout: 'app',
})

const today = new Date()
today.setHours(today.getHours(), 0, 0, 0)

const tomorrow = new Date(today)
tomorrow.setDate(today.getDate() + 1)

const router = useRouter()
const toast = useToast()
const colorMode = useColorMode()
const { df, tf } = useFormatter()
const basket = useBookingBasket()

const selection = ref<NewBookingSlot>()
const confirmation = ref({
  message: '',
  visible: false,
})
const helpVisible = ref(false)
const helpMaximized = ref(false)
const builderOpen = ref(false)

const { data: help } = await useLazyAsyncData(() => queryCollection('content').path('/help').first())
const { data: facilities, isPending: facilitiesIsPending } = useGetFacility()
const { data: bookings, isPending: bookingsIsPending } = useGetBooking()

const facilityTypes = computed(() => {
  if (facilitiesIsPending.value) {
    return []
  }

  return facilities.value?.map(f => f.group).filter((v, i, a): v is string => !!v && a.indexOf(v) === i)
})

const { $driver } = useNuxtApp()
const onboarded = useLocalStorage<boolean>('new-index-onboarded', false)

const facilityType = useRouteQuery<string>('facility-type')
const startDate = useRouteQuery('start-date')
const view = useRouteQuery('view', 'day')

onMounted(() => {
  if (!onboarded.value) {
    $driver.setConfig({
      showProgress: true,
      steps: [
        { element: '#facility-type', popover: { title: 'Facility type', description: 'Facilities are grouped into different types. Select one to view its schedule.' } },
        { element: '.vuecal__header', popover: { title: 'Date and time', description: 'Toggle the schedule for different date and times here.' } },
        { element: '.vuecal__schedule--cell', popover: { title: 'Schedule', description: 'The facility schedule will show up here.' } },
        { element: '.vuecal__time-column', popover: { title: 'Scroll', description: `If you're using a mobile device, use this area to scroll the timeline view.` } },
        {
          element: '#confirm-selection', popover: {
            title: 'Confirm selection',
            description: 'Once you have selected a time slot, click this button to confirm your booking.',
            onNextClick() {
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

const facilitiesUnderFacilityType = computed(() => {
  if (facilitiesIsPending.value) {
    return []
  }

  return facilities.value?.filter(n => n.group === facilityType.value)
})

const bookingsUnderFacilityType = computed(() => {
  if (bookingsIsPending.value) {
    return []
  }

  return bookings.value?.filter(n => facilitiesUnderFacilityType.value?.some(nn => nn.name == n.facilityName))
})

function scheduleOf(facilityName: string) {
  return (facilitiesUnderFacilityType.value?.findIndex(n => n.name === facilityName) ?? -1) + 1
}

const calOptions = computed(() => {
  const events = []

  for (const booking of bookingsUnderFacilityType.value ?? []) {
    events.push({
      id: booking?.id,
      start: booking.startDateTime,
      end: booking.endDateTime,
      schedule: facilitiesUnderFacilityType.value?.findIndex(n => n.name === booking.facilityName) + 1,
      title: booking?.user?.unit + ' / ' + booking?.conduct,
      content: '<br>' + booking?.description + '<br>' + booking?.pocName + '<br>' + booking?.pocPhone,
      draggable: false,
      resizable: false,
      deletable: false,
    })
  }

  // Slots already added to the list
  for (const slot of basket.slots.value) {
    const schedule = scheduleOf(slot.facilityName)
    if (!schedule) continue
    events.push({
      id: `basket-${slot.id}`,
      start: slot.start,
      end: slot.end,
      schedule,
      title: 'In your list',
      class: 'queued-booking',
      draggable: false,
      resizable: false,
      deletable: false,
    })
  }

  // The slot being picked, kept here rather than inside vue-cal so it survives data refreshes
  if (selection.value && scheduleOf(selection.value.facilityName)) {
    events.push({
      id: 'new-booking',
      start: new Date(selection.value.start),
      end: new Date(selection.value.end),
      schedule: scheduleOf(selection.value.facilityName),
      title: 'New booking',
      class: 'new-booking',
      draggable: true,
      resizable: true,
      deletable: false,
    })
  }

  return {
    view: view.value,
    views: ['day', 'week'],
    viewDate: startDate.value,
    snapToInterval: 30,
    eventCreateMinDrag: 20,
    timeStep: 30,
    editableEvents: true,
    minDate: today,
    dark: colorMode.value === 'dark',
    schedules: facilities.value?.filter(n => n.group === facilityType.value).map(name => ({ label: name.name })),
    events,
    onReady,
    onEventCreate,
    onEventResizeEnd,
    onEventDrop,
    style: 'flex: 1',
  }
})

const basketClashes = computed(() => findClashes(basket.slots.value, bookings.value))
const basketClashCount = computed(() => basketClashes.value.filter(Boolean).length)

const clashMessages: Record<SlotClash['kind'], string> = {
  past: 'This time has already passed',
  existing: 'Clashes with an existing booking',
  selection: 'Overlaps a slot in your list',
}

const selectionSummary = computed(() => {
  if (!selection.value) return undefined

  const clash = findClashes([...basket.slots.value, selection.value], bookings.value).at(-1)
  return {
    facilityName: selection.value.facilityName,
    ...describeSlot(selection.value, df, tf),
    clash: clash && clashMessages[clash.kind],
  }
})

const selectionIsWholeDay = computed(() => {
  if (!selection.value) return false
  const whole = wholeDay(selection.value.start)
  return whole.start.getTime() === selection.value.start.getTime() && whole.end.getTime() === selection.value.end.getTime()
})

const slotCount = computed(() => basket.slots.value.length + (selection.value ? 1 : 0))
const continueLabel = computed(() => basket.slots.value.length
  ? `Continue with ${slotCount.value} ${slotCount.value === 1 ? 'slot' : 'slots'}`
  : 'Confirm selection')

whenever(facilityTypes, (f) => {
  if (!facilityType.value && f[0]) {
    facilityType.value = f[0]
  }
}, { immediate: true })

watch(facilityType, () => {
  selection.value = undefined
})

function onReady({ view }) {
  view.scrollToCurrentTime()
}

function selectWholeDay() {
  if (!selection.value) return

  const { start, end } = wholeDay(selection.value.start)
  if (end <= start) {
    toast.add({ title: 'This day is almost over.', description: 'Pick a later day to book it whole.', color: 'warning', icon: 'i-lucide-triangle-alert', duration: 3000 })
    return
  }

  selection.value = { facilityName: selection.value.facilityName, start, end }
}

function hasRoomFor(count: number) {
  if (basket.slots.value.length + count <= MAX_BATCH_SLOTS) return true

  toast.add({
    title: `You can book up to ${MAX_BATCH_SLOTS} slots at a time.`,
    color: 'warning',
    icon: 'i-lucide-triangle-alert',
    duration: 3000,
  })
  return false
}

function addSelectionToList() {
  if (!selection.value || !hasRoomFor(1)) return

  basket.add([selection.value])
  selection.value = undefined
}

function onBuilderAdd(slots: NewBookingSlot[]) {
  if (!hasRoomFor(slots.length)) return

  basket.add(slots)
  toast.add({
    title: `Added ${slots.length} ${slots.length === 1 ? 'slot' : 'slots'} to your list`,
    color: 'success',
    icon: 'i-lucide-circle-check',
    duration: 3000,
  })
}

function removeFromList(index: number) {
  const slot = basket.slots.value[index]
  if (slot) basket.remove([slot.id])
}

function removeClashing() {
  basket.remove(basket.slots.value.filter((_, i) => basketClashes.value[i]).map(s => s.id))
}

function confirmSelection(fromDialog: boolean) {
  const slots = [...basket.slots.value, ...(selection.value ? [selection.value] : [])]
  if (!slots.length) {
    return
  }

  const showEigerConfirmation = slots.some(s => s.facilityName === 'Eiger' || s.facilityName === 'Temasek Square')
  if (showEigerConfirmation && !fromDialog) {
    confirmation.value = {
      visible: true,
      message: 'Eiger refers to the running route. Temasek Square refers to the center parade square area. Did you select the correct facility?',
    }
    return
  }

  if (fromDialog) {
    confirmation.value = {
      visible: false,
      message: '',
    }
  }

  // A single slot goes straight to the confirm page, as before
  if (!basket.slots.value.length && selection.value) {
    router.push({
      path: '/booking/new/confirm',
      query: {
        ['start-date']: selection.value.start.toISOString(),
        ['end-date']: selection.value.end.toISOString(),
        ['facility-name']: selection.value.facilityName,
        ['original-query']: window.location.search,
      },
    })
    return
  }

  if (selection.value) {
    if (!hasRoomFor(1)) return
    basket.add([selection.value])
    selection.value = undefined
  }

  router.push({
    path: '/booking/new/confirm',
    query: { ['original-query']: window.location.search },
  })
}

function facilityOf(event: { schedule: number }) {
  const facilityName = facilitiesUnderFacilityType.value?.[event.schedule - 1]?.name
  if (!facilityName) {
    throw new Error('Invalid facility name')
  }
  return facilityName
}

async function onEventResizeEnd({ event, ...rest }) {
  if (rest.overlaps.length) {
    return false
  }

  selection.value = { start: new Date(event.start), end: new Date(event.end), facilityName: facilityOf(event) }
  return true
}

async function onEventDrop({ event, ...rest }) {
  if (rest.overlaps.length) {
    return false
  }

  selection.value = { start: new Date(event.start), end: new Date(event.end), facilityName: facilityOf(event) }
  return true
}

async function onEventCreate({ event, resolve }) {
  selection.value = { start: new Date(event.start), end: new Date(event.end), facilityName: facilityOf(event) }

  // The selection is rendered from `calOptions.events`, so don't let vue-cal keep its own copy
  resolve(false)
}

async function eventDoubleClick({ event }) {
  if (event.id === 'new-booking' || String(event.id).startsWith('basket-')) {
    return
  }

  await router.push(`/booking/${event.id}`)
}

function onViewChange({ start, id }) {
  startDate.value = start
  view.value = id
  selection.value = undefined
}
</script>

<template>
  <UDashboardPanel id="booking-new">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: '/booking' },
              { label: 'New', to: '/booking/new' },
            ]"
          />
        </template>

        <template #right>
          <UTooltip text="Help">
            <UButton
              color="neutral"
              variant="ghost"
              icon="i-lucide-circle-help"
              aria-label="Help"
              @click="helpVisible = true"
            />
          </UTooltip>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <UModal
        v-model:open="confirmation.visible"
        title="Are you sure?"
        :description="confirmation.message"
      >
        <template #footer>
          <div class="flex w-full justify-end gap-2">
            <UButton
              label="No"
              color="neutral"
              variant="outline"
              autofocus
              @click="confirmation.visible = false"
            />
            <UButton
              label="Yes"
              @click="confirmSelection(true)"
            />
          </div>
        </template>
      </UModal>

      <UModal
        v-model:open="helpVisible"
        title="Help"
        :fullscreen="helpMaximized"
        :ui="{ body: 'sm:p-6' }"
      >
        <template #actions>
          <UButton
            color="neutral"
            variant="ghost"
            :icon="helpMaximized ? 'i-lucide-minimize-2' : 'i-lucide-maximize-2'"
            :aria-label="helpMaximized ? 'Restore' : 'Maximise'"
            class="hidden sm:inline-flex"
            @click="helpMaximized = !helpMaximized"
          />
        </template>

        <template #body>
          <article>
            <ContentRenderer
              v-if="help"
              :value="help"
            />
          </article>
        </template>
      </UModal>

      <BookingBuilder
        v-model:open="builderOpen"
        @add="onBuilderAdd"
      />

      <div class="flex flex-wrap items-end justify-between gap-3">
        <UFormField
          label="Facility Type"
          class="w-full sm:w-72"
        >
          <USelect
            id="facility-type"
            v-model="facilityType"
            :items="facilityTypes"
            :loading="facilitiesIsPending"
            placeholder="Select a facility type"
            class="w-full"
          />
        </UFormField>

        <div class="flex w-full items-center justify-between gap-3 sm:w-auto">
          <p class="hidden lg:flex items-center gap-1.5 text-sm text-muted">
            <UIcon
              name="i-lucide-mouse-pointer-click"
              class="size-4"
            />
            Click and drag on the timeline to select a slot. Double-click a booking to view it.
          </p>

          <UButton
            id="book-multiple"
            label="Book multiple"
            icon="i-lucide-calendar-range"
            color="neutral"
            variant="outline"
            class="w-full justify-center sm:w-auto"
            @click="builderOpen = true"
          />
        </div>
      </div>

      <div class="relative flex flex-1 min-h-80">
        <div class="absolute inset-0">
          <VueCal
            v-bind="calOptions"
            @event-dblclick="eventDoubleClick"
            @view-change="onViewChange"
          >
            <template #schedule-heading="{ schedule }">
              <strong
                class="block w-full truncate px-1 text-center"
                :title="schedule.label"
              >{{ schedule.label }}</strong>
            </template>
          </VueCal>
        </div>
      </div>

      <section
        v-if="basket.slots.value.length"
        aria-label="Your list"
        class="rounded-lg border border-default bg-elevated/30"
      >
        <div class="flex items-center justify-between gap-2 border-b border-default px-3 py-2">
          <h2 class="text-sm font-semibold text-highlighted">
            Your list
            <UBadge
              :label="basket.slots.value.length"
              variant="subtle"
              size="sm"
              class="ms-1"
            />
          </h2>
          <div class="flex items-center gap-1">
            <UButton
              v-if="basketClashCount"
              :label="`Remove ${basketClashCount} unavailable`"
              color="error"
              variant="ghost"
              size="xs"
              @click="removeClashing"
            />
            <UButton
              label="Clear"
              color="neutral"
              variant="ghost"
              size="xs"
              @click="basket.clear()"
            />
          </div>
        </div>
        <BookingSlotList
          :slots="basket.slots.value"
          :clashes="basketClashes"
          removable
          class="max-h-32 overflow-y-auto px-2"
          @remove="removeFromList"
        />
      </section>

      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0 text-sm">
          <template v-if="selectionSummary">
            <p class="font-medium text-highlighted truncate">
              {{ selectionSummary.facilityName }}
            </p>
            <p class="text-muted">
              {{ selectionSummary.date }} · {{ selectionSummary.time }}
            </p>
            <p
              v-if="selectionSummary.clash"
              class="text-xs text-error"
            >
              {{ selectionSummary.clash }}
            </p>
          </template>
          <p
            v-else
            class="text-muted"
          >
            {{ basket.slots.value.length ? 'Pick another slot, or continue with your list.' : 'No time slot selected yet.' }}
          </p>
        </div>

        <div class="flex flex-wrap items-center gap-2 sm:flex-nowrap sm:justify-end">
          <template v-if="selection">
            <UButton
              label="All day"
              icon="i-lucide-unfold-vertical"
              color="neutral"
              variant="outline"
              :disabled="selectionIsWholeDay"
              @click="selectWholeDay"
            />
            <UButton
              label="Add to list"
              icon="i-lucide-list-plus"
              color="neutral"
              variant="outline"
              @click="addSelectionToList"
            />
          </template>
          <UButton
            id="confirm-selection"
            :label="continueLabel"
            trailing-icon="i-lucide-arrow-right"
            size="lg"
            class="w-full justify-center sm:w-auto sm:min-w-56"
            :disabled="!slotCount"
            @click="confirmSelection(false)"
          />
        </div>
      </div>
    </template>
  </UDashboardPanel>
</template>

<style scoped>
.vuecal {
  --vuecal-primary-color: var(--ui-primary);
  --vuecal-secondary-color: var(--ui-bg);
  --vuecal-base-color: var(--ui-text);
  --vuecal-contrast-color: var(--ui-bg);
  --vuecal-border-color: var(--ui-border);
  --vuecal-header-color: var(--ui-text-highlighted);
  --vuecal-event-color: var(--ui-text-highlighted);
  --vuecal-event-border-color: transparent;
  --vuecal-border-radius: calc(var(--ui-radius) * 2);
  --vuecal-height: 100%;
}

/* Header: neutral bars with Nuxt UI style controls instead of the solid primary default */
:deep(.vuecal__header) {
  background-color: var(--ui-bg-elevated);
  color: var(--ui-text-highlighted);
  border: 1px solid var(--ui-border);
  border-bottom: none;
}

:deep(.vuecal__views-bar) {
  padding: 0.5rem;
  border-bottom: 1px solid var(--ui-border);
}

:deep(.vuecal__view-button) {
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.025em;
  color: var(--ui-text-muted);
  padding: 0.25rem 0.875rem;
  border-radius: calc(var(--ui-radius) * 1.5);
}

:deep(.vuecal__view-button:hover),
:deep(.vuecal__nav:hover),
:deep(button.vuecal__title:hover) {
  background-color: var(--ui-bg-accented);
}

:deep(.vuecal__view-button--active),
:deep(.vuecal__view-button--active:hover) {
  background-color: var(--ui-bg);
  color: var(--ui-text-highlighted);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.08), 0 0 0 1px var(--ui-border);
}

:deep(.vuecal__title-bar) {
  background-color: transparent;
  padding: 0.375rem 0.5rem;
}

:deep(.vuecal__title) {
  font-weight: 600;
  font-size: clamp(0.75rem, 2.8vw, 0.9375rem);
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
}

:deep(.vuecal__schedule--heading) {
  min-width: 0;
}

:deep(.vuecal__nav) {
  color: var(--ui-text-muted);
}

:deep(.vuecal__nav--today) {
  font-size: 0.75rem;
  font-weight: 600;
  border: 1px solid var(--ui-border-accented) !important;
  border-radius: calc(var(--ui-radius) * 1.5);
  background-color: var(--ui-bg);
}

:deep(.vuecal__scrollable-wrap) {
  background-color: var(--ui-bg);
}

:deep(.vuecal__time-column),
:deep(.vuecal__headings) {
  color: var(--ui-text-muted);
}

/* Existing bookings: tinted with a primary accent. The user's own selection: solid primary. */
:deep(.vuecal__event) {
  background-color: color-mix(in oklab, var(--ui-primary) 16%, var(--ui-bg));
  border-inline-start: 3px solid var(--ui-primary);
  border-radius: calc(var(--ui-radius) * 1.25);
  padding: 0.125rem 0.25rem;
  font-size: 0.75rem;
  line-height: 1.25;
  touch-action: none;
}

:deep(.vuecal__event.new-booking) {
  background-color: var(--ui-primary);
  color: var(--ui-text-inverted);
  border-inline-start-color: color-mix(in oklab, var(--ui-primary) 70%, black);
  box-shadow: 0 4px 12px -4px color-mix(in oklab, var(--ui-primary) 60%, transparent);
  z-index: 2;
}

/* Slots already added to the list */
:deep(.vuecal__event.queued-booking) {
  background: repeating-linear-gradient(
    -45deg,
    color-mix(in oklab, var(--ui-primary) 22%, var(--ui-bg)),
    color-mix(in oklab, var(--ui-primary) 22%, var(--ui-bg)) 6px,
    color-mix(in oklab, var(--ui-primary) 10%, var(--ui-bg)) 6px,
    color-mix(in oklab, var(--ui-primary) 10%, var(--ui-bg)) 12px
  );
  border: 1px dashed var(--ui-primary);
  border-inline-start-width: 3px;
  border-inline-start-style: solid;
}

:deep(.vuecal__event-placeholder) {
  background-color: var(--ui-primary);
  color: var(--ui-text-inverted);
}

:deep(.vuecal__event-resizer) {
  height: 12px;
  background-color: rgba(255, 255, 255, 0.45);
  opacity: 0.85 !important;
  border-top: 1px solid rgba(0, 0, 0, 0.15);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background-color 0.2s, opacity 0.2s;
  z-index: 10;
}

:deep(.vuecal__event-resizer:hover) {
  background-color: rgba(255, 255, 255, 0.6);
  opacity: 1 !important;
}

/* Grab handle lines */
:deep(.vuecal__event-resizer::before) {
  content: "";
  width: 16px;
  height: 2px;
  background-color: rgba(0, 0, 0, 0.35);
  box-shadow: 0 4px 0 rgba(0, 0, 0, 0.35);
}

/* Expanded touch target for mobile/touch screens */
:deep(.vuecal__event-resizer::after) {
  content: "";
  position: absolute;
  top: -16px;
  bottom: -16px;
  left: 0;
  right: 0;
}
</style>
