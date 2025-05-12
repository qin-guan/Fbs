<script setup lang="ts">
import { useQuery } from '@urql/vue'
import { VueCal } from 'vue-cal'
import 'vue-cal/style'

const today = new Date()
today.setHours(today.getHours(), 0, 0, 0)

const tomorrow = new Date(today)
tomorrow.setDate(today.getDate() + 1)

const group = 'Outdoor'
const facilities = useQuery({
  query: queries.allSlots,
  variables: {
    startDate: today,
    endDate: tomorrow,
  },
})

const calOptions = computed(() => {
  const e = facilities.data.value?.facilities.filter(n => n.group === group).map((f, idx) => ({ bookings: f.availableTimeSlots.filter(s => s.booking), schedule: idx + 1 }))
  const events = []
  for (const facility of e ?? []) {
    for (const booking of facility.bookings) {
      const existing = events.find(e => e.id === booking.booking?.id)
      if (existing) {
        existing.start = new Date(Math.min(existing.start.getTime(), new Date(booking.startDateTime).getTime()))
        existing.end = new Date(Math.max(existing.end.getTime(), new Date(booking.endDateTime).getTime()))
        continue
      }
      events.push({
        id: booking.booking?.id,
        start: new Date(booking.startDateTime),
        end: new Date(booking.endDateTime),
        schedule: facility.schedule,
        title: booking.booking?.user?.unit + ' / ' + booking.booking?.conduct,
        content: booking.booking?.description + '<br>' + booking.booking?.pocName + '<br>' + booking.booking?.pocPhone,
        draggable: false,
        resizable: false,
        deletable: false,
      })
    }
  }
  return {
    views: ['day'],
    editableEvents: true,
    snapToInterval: 30,
    timeStep: 30,
    minDate: today,
    schedules: facilities.data.value?.facilities.filter(n => n.group === group).map(name => ({ label: name.name })),
    events,
    onReady,
    onEventCreate,
    style: 'height: 100%',
  }
})

function onReady({ view }) {
  view.scrollToCurrentTime()
}

function onEventCreate(e) {
  console.log(e.cell)
}
</script>

<template>
  <div class="h-full">
    <VueCal v-bind="calOptions">
      <template #schedule-heading="{ schedule }">
        <strong>{{ schedule.label }}</strong>
      </template>
    </VueCal>
  </div>
</template>
