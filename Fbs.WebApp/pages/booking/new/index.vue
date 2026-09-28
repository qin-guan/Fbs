<script setup lang="ts">
import { VueCal } from 'vue-cal'
import 'vue-cal/style'

definePageMeta({
  layout: 'app',
})

const today = new Date()
today.setHours(today.getHours(), 0, 0, 0)

const tomorrow = new Date(today)
tomorrow.setDate(today.getDate() + 1)

const router = useRouter()
const colorMode = useColorMode()
const { df, tf } = useFormatter()
const cal = useTemplateRef('cal')

const selection = ref<{ start: Date, end: Date, facilityName: string }>()
const confirmation = ref({
  message: '',
  visible: false,
})
const helpVisible = ref(false)
const helpMaximized = ref(false)

const { data: help } = await useLazyAsyncData(() => queryCollection('content').path('/help').first())
const { data: facilities, isPending: facilitiesIsPending } = useFacilities()
const { data: bookings, isPending: bookingsIsPending } = useBookings()

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

const selectionSummary = computed(() => {
  if (!selection.value) return undefined

  const { start, end, facilityName } = selection.value
  const sameDay = start.toDateString() === end.toDateString()
  const range = sameDay
    ? `${df.format(start)}, ${tf.format(start)} – ${tf.format(end)}`
    : `${df.format(start)}, ${tf.format(start)} – ${df.format(end)}, ${tf.format(end)}`

  return { facilityName, range }
})

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

function confirmSelection(fromDialog: boolean) {
  if (!selection.value) {
    return
  }

  const showEigerConfirmation = selection.value.facilityName === 'Eiger' || selection.value.facilityName === 'Temasek Square'
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

  router.push({
    path: '/booking/new/confirm',
    query: {
      ['start-date']: selection.value.start.toISOString(),
      ['end-date']: selection.value.end.toISOString(),
      ['facility-name']: selection.value.facilityName,
      ['original-query']: window.location.search,
    },
  })
}

async function onEventResizeEnd({ event, ...rest }) {
  const facilityName = facilitiesUnderFacilityType.value?.[event.schedule - 1]?.name
  if (!facilityName) {
    throw new Error('Invalid facility name')
  }

  selection.value = {
    start: event.start,
    end: event.end,
    facilityName,
  }

  return !rest.overlaps.length
}

async function onEventDrop({ event, ...rest }) {
  const facilityName = facilitiesUnderFacilityType.value?.[event.schedule - 1]?.name
  if (!facilityName) {
    throw new Error('Invalid facility name')
  }

  selection.value = {
    start: event.start,
    end: event.end,
    facilityName,
  }

  return !rest.overlaps.length
}

async function onEventCreate({ event, resolve }) {
  cal.value.view.deleteEvent({ id: 'new-booking' }, 3)

  const facilityName = facilitiesUnderFacilityType.value?.[event.schedule - 1]?.name
  if (!facilityName) {
    throw new Error('Invalid facility name')
  }

  selection.value = {
    start: event.start,
    end: event.end,
    facilityName,
  }

  resolve({
    ...event,
    id: 'new-booking',
    title: 'New booking',
    class: 'new-booking',
    resizable: true,
    draggable: true,
  })
}

async function eventDoubleClick({ event }) {
  if (event.id === 'new-booking') {
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

        <p class="hidden md:flex items-center gap-1.5 text-sm text-muted">
          <UIcon
            name="i-lucide-mouse-pointer-click"
            class="size-4"
          />
          Click and drag on the timeline to select a slot. Double-click a booking to view it.
        </p>
      </div>

      <div class="relative flex flex-1 min-h-[28rem]">
        <div class="absolute inset-0">
          <VueCal
            ref="cal"
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

      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0 text-sm">
          <template v-if="selectionSummary">
            <p class="font-medium text-highlighted truncate">
              {{ selectionSummary.facilityName }}
            </p>
            <p class="text-muted">
              {{ selectionSummary.range }}
            </p>
          </template>
          <p
            v-else
            class="text-muted"
          >
            No time slot selected yet.
          </p>
        </div>

        <UButton
          id="confirm-selection"
          label="Confirm selection"
          trailing-icon="i-lucide-arrow-right"
          size="lg"
          class="justify-center sm:min-w-56"
          :disabled="!selection?.start"
          @click="confirmSelection(false)"
        />
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
