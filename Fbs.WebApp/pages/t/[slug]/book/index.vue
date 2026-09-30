<script setup lang="ts">
import { VueCal } from 'vue-cal'
import 'vue-cal/style'
import { useGetOrgBookings, useGetOrgFacilitiesBookable } from '~/api'
import { fromDate } from '@internationalized/date'
import { addDaysIn, describeSlot, findClashes, fromWall, MAX_BATCH_SLOTS, startOfDayIn, toWall, wholeDay, type KnownBooking, type Slot, type SlotClash } from '~/lib/slots'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'New booking' })

const router = useRouter()
const toast = useToast()
const colorMode = useColorMode()
const { slug, org, timeZone, path } = useTenant()
const { df, tf } = useTenantFormatter()
const basket = useTenantBasket()
const now = useNow({ interval: 60_000 })

const zone = computed(() => ({ timeZone: timeZone.value, slotMinutes: org.value?.slotMinutes ?? 30 }))
const selection = ref<Slot>()
const builderOpen = ref(false)

const { data: facilities, isPending: facilitiesIsPending } = useGetOrgFacilitiesBookable({ path: computed(() => ({ slug: slug.value })) })

const facilityGroups = computed(() => [...new Set(facilities.value?.map(f => f.group ?? 'Other'))])
const facilityGroup = useRouteQuery<string>('facility-type')
whenever(facilityGroups, (groups) => {
  if (!facilityGroup.value || !groups.includes(facilityGroup.value)) {
    facilityGroup.value = groups[0] ?? ''
  }
}, { immediate: true })
watch(facilityGroup, () => {
  selection.value = undefined
})

const inGroup = computed(() => facilities.value?.filter(f => (f.group ?? 'Other') === facilityGroup.value) ?? [])
const scheduleOf = (facilityId: string) => inGroup.value.findIndex(f => f.id === facilityId) + 1

// The calendar draws the time on the wall in the organization, whichever time zone the browser is in
const startDate = useRouteQuery('start-date')
const view = useRouteQuery<string>('view', 'day')
const viewWindow = ref<{ from: Date, to: Date }>()
const window = computed(() => viewWindow.value ?? { from: startOfDayIn(now.value, timeZone.value), to: addDaysIn(startOfDayIn(now.value, timeZone.value), 7, timeZone.value) })

const { data: bookings } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: window.value.from, to: window.value.to, mine: false })),
})

// The slots in the list may be on other days than the ones on show, so what they would clash with is read for their days too
const basketSpan = computed(() => {
  const slots = basket.slots.value
  if (!slots.length) {
    return undefined
  }

  return { from: new Date(Math.min(...slots.map(s => s.start.getTime()))), to: new Date(Math.max(...slots.map(s => s.end.getTime()))) }
})
const basketSpanFits = computed(() => !!basketSpan.value && basketSpan.value.to.getTime() - basketSpan.value.from.getTime() <= 93 * 86_400_000)
const { data: basketBookings } = useGetOrgBookings({
  path: computed(() => ({ slug: slug.value })),
  query: computed(() => ({ from: basketSpan.value?.from, to: basketSpan.value?.to, mine: false })),
}, { query: { enabled: basketSpanFits } })

const known = computed<KnownBooking[]>(() => {
  const byId = new Map<string, KnownBooking>()
  for (const b of [...(bookings.value ?? []), ...(basketBookings.value ?? [])]) {
    byId.set(b.id, { id: b.id, facilityId: b.facilityId, startDateTime: b.startDateTime, endDateTime: b.endDateTime, conduct: b.conduct, bookedBy: b.bookedBy })
  }
  return [...byId.values()]
})

const calOptions = computed(() => {
  const events = []

  for (const booking of bookings.value ?? []) {
    const schedule = scheduleOf(booking.facilityId)
    if (!schedule) continue
    events.push({
      id: booking.id,
      start: toWall(booking.startDateTime, timeZone.value),
      end: toWall(booking.endDateTime, timeZone.value),
      schedule,
      title: `${booking.bookedBy.displayName} / ${booking.conduct}`,
      content: [booking.description, booking.pocName, booking.pocPhone].filter(Boolean).map(t => `<br>${t}`).join(''),
      draggable: false,
      resizable: false,
      deletable: false,
    })
  }

  // Slots already added to the list
  for (const slot of basket.slots.value) {
    const schedule = scheduleOf(slot.facilityId)
    if (!schedule) continue
    events.push({
      id: `basket-${slot.id}`,
      start: toWall(slot.start, timeZone.value),
      end: toWall(slot.end, timeZone.value),
      schedule,
      title: 'In your list',
      class: 'queued-booking',
      draggable: false,
      resizable: false,
      deletable: false,
    })
  }

  // The slot being picked, kept here rather than inside vue-cal so it survives data refreshes
  if (selection.value && scheduleOf(selection.value.facilityId)) {
    events.push({
      id: 'new-booking',
      start: toWall(selection.value.start, timeZone.value),
      end: toWall(selection.value.end, timeZone.value),
      schedule: scheduleOf(selection.value.facilityId),
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
    viewDate: startDate.value || toWall(now.value, timeZone.value),
    snapToInterval: zone.value.slotMinutes,
    eventCreateMinDrag: 20,
    timeStep: zone.value.slotMinutes,
    editableEvents: true,
    minDate: toWall(now.value, timeZone.value),
    dark: colorMode.value === 'dark',
    schedules: inGroup.value.map(f => ({ label: f.name })),
    events,
    onReady,
    onEventCreate,
    onEventResizeEnd,
    onEventDrop,
    style: 'flex: 1',
  }
})

// The calendar works from the browser's clock for what it draws of now, which is only right if the browser is where the organization is
const zoneDiffers = computed(() => fromDate(now.value, timeZone.value).offset !== -now.value.getTimezoneOffset() * 60_000)

const facilityNames = computed(() => facilities.value?.map(f => ({ id: f.id, name: f.name })) ?? [])
const nameOf = (facilityId: string) => facilityNames.value.find(f => f.id === facilityId)?.name ?? ''

const basketClashes = computed(() => findClashes(basket.slots.value, known.value, now.value))
const basketClashCount = computed(() => basketClashes.value.filter(Boolean).length)

const clashMessages: Record<SlotClash['kind'], string> = {
  past: 'This time has already passed',
  existing: 'Clashes with an existing booking',
  selection: 'Overlaps a slot in your list',
}

const selectionSummary = computed(() => {
  if (!selection.value) return undefined

  const clash = findClashes([...basket.slots.value, selection.value], known.value, now.value).at(-1)
  return {
    facilityName: nameOf(selection.value.facilityId),
    ...describeSlot(selection.value, df.value, tf.value, timeZone.value),
    clash: clash && clashMessages[clash.kind],
  }
})

const selectionIsWholeDay = computed(() => {
  if (!selection.value) return false
  const whole = wholeDay(selection.value.start, now.value, zone.value)
  return whole.start.getTime() === selection.value.start.getTime() && whole.end.getTime() === selection.value.end.getTime()
})

const slotCount = computed(() => basket.slots.value.length + (selection.value ? 1 : 0))
const continueLabel = computed(() => basket.slots.value.length
  ? `Continue with ${slotCount.value} ${slotCount.value === 1 ? 'slot' : 'slots'}`
  : 'Confirm selection')

function openBuilder() {
  builderOpen.value = true
}

function onReady({ view }: { view: { scrollToCurrentTime: () => void } }) {
  view.scrollToCurrentTime()
}

function selectWholeDay() {
  if (!selection.value) return

  const { start, end } = wholeDay(selection.value.start, now.value, zone.value)
  if (end <= start) {
    toast.add({ title: 'This day is almost over.', description: 'Pick a later day to book it whole.', color: 'warning', icon: 'i-lucide-triangle-alert', duration: 3000 })
    return
  }

  selection.value = { facilityId: selection.value.facilityId, start, end }
}

function hasRoomFor(count: number) {
  if (basket.slots.value.length + count <= MAX_BATCH_SLOTS) return true

  toast.add({ title: `You can book up to ${MAX_BATCH_SLOTS} slots at a time.`, color: 'warning', icon: 'i-lucide-triangle-alert', duration: 3000 })
  return false
}

function addSelectionToList() {
  if (!selection.value || !hasRoomFor(1)) return

  basket.add([selection.value])
  selection.value = undefined
}

function onBuilderAdd(slots: Slot[]) {
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

async function confirmSelection() {
  if (!slotCount.value) {
    return
  }

  if (selection.value) {
    if (!hasRoomFor(1)) return
    basket.add([selection.value])
    selection.value = undefined
  }

  await router.push(path('book', 'confirm'))
}

function facilityOf(event: { schedule: number }) {
  const facility = inGroup.value[event.schedule - 1]
  if (!facility) {
    throw new Error('Invalid facility')
  }
  return facility.id
}

function pick(event: { start: Date | string, end: Date | string, schedule: number }) {
  selection.value = { facilityId: facilityOf(event), start: fromWall(new Date(event.start), timeZone.value), end: fromWall(new Date(event.end), timeZone.value) }
}

async function onEventResizeEnd({ event, ...rest }: { event: { start: Date, end: Date, schedule: number }, overlaps: unknown[] }) {
  if (rest.overlaps.length) {
    return false
  }

  pick(event)
  return true
}

async function onEventDrop({ event, ...rest }: { event: { start: Date, end: Date, schedule: number }, overlaps: unknown[] }) {
  if (rest.overlaps.length) {
    return false
  }

  pick(event)
  return true
}

async function onEventCreate({ event, resolve }: { event: { start: Date, end: Date, schedule: number }, resolve: (keep: boolean) => void }) {
  pick(event)

  // The selection is rendered from `calOptions.events`, so don't let vue-cal keep its own copy
  resolve(false)
}

async function eventDoubleClick({ event }: { event: { id: string | number } }) {
  if (event.id === 'new-booking' || String(event.id).startsWith('basket-')) {
    return
  }

  await router.push(path('bookings', String(event.id)))
}

function onViewChange({ start, end, id }: { start: Date, end: Date, id: string }) {
  startDate.value = new Date(start).toISOString()
  view.value = id
  selection.value = undefined
  viewWindow.value = { from: fromWall(new Date(start), timeZone.value), to: fromWall(new Date(end), timeZone.value) }
}
</script>

<template>
  <UDashboardPanel id="booking-new">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: path() },
              { label: 'New', to: path('book') },
            ]"
          />
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <TenantBookingBuilder
        v-model:open="builderOpen"
        @add="onBuilderAdd"
      />

      <UAlert
        v-if="!facilitiesIsPending && !facilities?.length"
        title="There is nothing you can book yet"
        description="Ask an admin to add facilities, or to give your unit access to them."
        color="warning"
        variant="subtle"
        icon="i-lucide-info"
      />

      <div class="flex flex-wrap items-end justify-between gap-3">
        <UFormField
          label="Facility Type"
          class="w-full sm:w-72"
        >
          <USelect
            id="facility-type"
            v-model="facilityGroup"
            :items="facilityGroups"
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
            @click="openBuilder"
          />
        </div>
      </div>

      <p
        v-if="zoneDiffers"
        class="flex items-center gap-1.5 text-sm text-muted"
      >
        <UIcon
          name="i-lucide-globe"
          class="size-4"
        />
        Times are in {{ timeZone }}, where {{ org?.name }} is, not where you are.
      </p>

      <div
        class="relative flex flex-1 min-h-80"
        :class="{ 'zone-differs': zoneDiffers }"
      >
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
        <TenantSlotList
          :slots="basket.slots.value"
          :facilities="facilityNames"
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
            @click="confirmSelection"
          />
        </div>
      </div>
    </template>
  </UDashboardPanel>
</template>

<style scoped>
/* What it draws of now, and where its Today button goes, are the browser's */
.zone-differs :deep(.vuecal__now-line),
.zone-differs :deep(.vuecal__nav--today) {
  display: none;
}

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
