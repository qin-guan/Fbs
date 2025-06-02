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
const cal = useTemplateRef('cal')

const selection = ref<{ start: Date, end: Date, facilityName: string }>()
const confirmation = ref({
  message: '',
  visible: false,
})
const helpVisible = ref(false)

const { data: help } = await useLazyAsyncData(() => queryCollection('content').path('/help').first())
const { data: facilities, isPending: facilitiesIsPending } = useFacilities()
const { data: bookings, isPending: bookingsIsPending } = useBookings()

const facilityTypes = computed(() => {
  if (facilitiesIsPending.value) {
    return []
  }

  return facilities.value?.map(f => f.group).filter((v, i, a) => a.indexOf(v) === i)
})

const { $driver } = useNuxtApp()
const onboarded = useLocalStorage<boolean>('new-index-onboarded', false)

const facilityType = useRouteQuery('facility-type')
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
    schedules: facilities.value?.filter(n => n.group === facilityType.value).map(name => ({ label: name.name })),
    events,
    onReady,
    onEventCreate,
		onEventResizeEnd,
    style: 'flex: 1',
  }
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

async function onEventCreate({ event, resolve, ...rest }) {
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
  <div class="h-full flex flex-col">
    <Dialog
      v-model:visible="confirmation.visible"
      modal
      header="Are you sure?"
    >
      <span>{{ confirmation.message }}</span>
      <template #footer>
        <Button
          label="No"
          autofocus
          outlined
          @click="confirmation.visible = false"
        />
        <Button
          label="Yes"
          @click="confirmSelection(true)"
        />
      </template>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <Breadcrumb
            :pt="{ root: { style: 'padding: 0;' } }"
            :model="[
              { label: 'Bookings', route: '/booking' },
              { label: 'New', route: '/booking/new' },
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
              <a
                v-else
                :href="item.url"
                :target="item.target"
                v-bind="props.action"
              >
                {{ item.label }}
              </a>
            </template>
          </Breadcrumb>

          <Button
            v-tooltip.bottom="'Help'"
            variant="text"
            @click="helpVisible = true"
          >
            <template #icon>
              <Icon name="i-lucide-circle-help" />
            </template>
          </Button>
        </div>
      </template>
    </AppNavbar>

    <LazyDialog
      v-model:visible="helpVisible"
      modal
      header="Help"
      maximizable
    >
      <article class="prose-sm">
        <ContentRenderer
          v-if="help"
          :value="help"
        />
      </article>
    </LazyDialog>

    <div class="p-3 flex flex-col flex-1 gap-3">
      <h2 class="text-lg font-semibold">
        New booking
      </h2>

      <FloatLabel variant="on">
        <Select
          id="facility-type"
          v-model="facilityType"
          :options="facilityTypes"
          placeholder="Select a facility type"
          filter
          fluid
        />
        <label for="facility-type">Facility Type</label>
      </FloatLabel>

      <div class="flex flex-1 relative">
        <div class="absolute inset-0">
          <VueCal
            ref="cal"
            v-bind="calOptions"
            @event-dblclick="eventDoubleClick"
            @view-change="onViewChange"
          >
            <template #schedule-heading="{ schedule }">
              <strong>{{ schedule.label }}</strong>
            </template>
          </VueCal>
        </div>
      </div>

      <Button
        id="confirm-selection"
        fluid
        label="Confirm selection"
        :disabled="!selection?.start"
        @click="confirmSelection(false)"
      />
    </div>
  </div>
</template>

<style scoped>
.vuecal {
  --vuecal-primary-color: var(--p-primary-color);
  --vuecal-height: 100%;
}

:deep(.vuecal__event-placeholder) {
  background-color: var(--p-primary-color);
}
</style>
