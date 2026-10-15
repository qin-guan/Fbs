<script setup lang="ts">
// A picture of the timeline for the home page, drawn with HTML so it is sharp at any size and costs no download. The day runs from
// 8:00 to 18:00, and a booking is placed along it by its hours.
const start = 8
const hours = 10
const now = 11.5

type Tone = 'primary' | 'info' | 'success' | 'warning'

interface Slot {
  from: number
  to: number
  label: string
  tone: Tone
}

const facilities: { name: string, kind: string, slots: Slot[], picking?: Omit<Slot, 'tone'> }[] = [
  {
    name: 'Meeting Room A',
    kind: 'Meeting room',
    slots: [
      { from: 9, to: 10.5, label: 'Team stand-up', tone: 'primary' },
      { from: 13, to: 14, label: 'Client call', tone: 'info' },
    ],
  },
  {
    name: 'Main Hall',
    kind: 'Hall',
    slots: [
      { from: 8, to: 11, label: 'Rehearsal', tone: 'warning' },
      { from: 14, to: 17, label: 'Town hall', tone: 'info' },
    ],
  },
  {
    name: 'Court 1',
    kind: 'Sports court',
    slots: [
      { from: 12, to: 13.5, label: 'Lunch game', tone: 'success' },
      { from: 16.5, to: 18, label: 'Training', tone: 'primary' },
    ],
  },
  {
    name: 'Science Lab 2',
    kind: 'Lab',
    slots: [{ from: 9.5, to: 11.5, label: 'Workshop', tone: 'success' }],
    picking: { from: 13.5, to: 15, label: 'New booking' },
  },
  {
    name: 'Training Room',
    kind: 'Classroom',
    slots: [
      { from: 10, to: 12, label: 'Onboarding', tone: 'info' },
      { from: 15, to: 16.5, label: 'Briefing', tone: 'warning' },
    ],
  },
]

const tones: Record<Tone, string> = {
  primary: 'bg-primary/15 text-primary ring-primary/30',
  info: 'bg-info/15 text-info ring-info/30',
  success: 'bg-success/15 text-success ring-success/30',
  warning: 'bg-warning/15 text-warning ring-warning/30',
}

const along = (hour: number) => `${((hour - start) / hours) * 100}%`
const place = (slot: { from: number, to: number }) => ({ left: along(slot.from), width: `${((slot.to - slot.from) / hours) * 100}%` })

// Every other one is left out on a phone, where there isn't room for them all
const ticks = [8, 10, 12, 14, 16, 18]
const tickLabel = (hour: number) => `${String(hour).padStart(2, '0')}:00`

// A line at each hour, behind the bookings
const hourLines = {
  backgroundImage: 'linear-gradient(to right, var(--ui-border) 1px, transparent 1px)',
  backgroundSize: `${100 / hours}% 100%`,
}
</script>

<template>
  <div
    class="relative"
    role="img"
    aria-label="The Book A Space timeline: a day of bookings across meeting rooms, a hall, a sports court, a lab and a classroom, with a new booking being picked out"
  >
    <div
      class="pointer-events-none absolute inset-x-0 -top-16 -bottom-8 -z-10 bg-[radial-gradient(closest-side,color-mix(in_oklab,var(--ui-primary)_22%,transparent),transparent)] dark:opacity-60"
      aria-hidden="true"
    />

    <div class="overflow-hidden rounded-2xl bg-default shadow-2xl shadow-primary/10 ring ring-default">
      <!-- The window it is in -->
      <div class="flex items-center gap-3 border-b border-default bg-elevated/60 px-4 py-2.5">
        <div class="flex gap-1.5">
          <span class="size-2.5 rounded-full bg-error/70" />
          <span class="size-2.5 rounded-full bg-warning/70" />
          <span class="size-2.5 rounded-full bg-success/70" />
        </div>
        <div class="mx-auto flex min-w-0 items-center gap-1.5 rounded-md bg-default px-3 py-1 text-xs text-muted ring ring-default">
          <UIcon
            name="i-lucide-lock"
            class="size-3 shrink-0"
          />
          <span class="truncate">bookaspace.app/t/northfield/timeline</span>
        </div>
        <div class="hidden w-12 sm:block" />
      </div>

      <div class="flex items-center justify-between gap-3 px-4 pt-4 sm:px-6">
        <div class="min-w-0">
          <p class="font-semibold text-highlighted">
            Timeline
          </p>
          <p class="truncate text-xs text-muted">
            Thursday · 5 facilities · 9 bookings
          </p>
        </div>
        <div class="flex shrink-0 items-center gap-2 text-xs">
          <span class="hidden rounded-md bg-elevated px-2.5 py-1 font-medium text-highlighted ring ring-default sm:inline">Day</span>
          <span class="hidden rounded-md px-2.5 py-1 text-muted sm:inline">Week</span>
          <span class="inline-flex items-center gap-1 rounded-md bg-primary px-2.5 py-1 font-medium text-inverted">
            <UIcon
              name="i-lucide-plus"
              class="size-3.5"
            />
            Book
          </span>
        </div>
      </div>

      <div class="grid grid-cols-[6rem_1fr] px-4 pt-4 pb-5 sm:grid-cols-[10rem_1fr] sm:px-6">
        <!-- The hours along the top, and now -->
        <div />
        <div class="relative h-6 text-[10px] text-dimmed sm:text-xs">
          <span
            v-for="tick in ticks"
            :key="tick"
            class="absolute top-0"
            :class="[
              tick === start ? '' : tick === start + hours ? '-translate-x-full' : '-translate-x-1/2',
              (tick - start) % 4 === 0 ? '' : 'hidden sm:inline',
            ]"
            :style="{ left: along(tick) }"
          >{{ tickLabel(tick) }}</span>
        </div>

        <template
          v-for="facility in facilities"
          :key="facility.name"
        >
          <div class="flex h-12 min-w-0 flex-col justify-center border-t border-default pr-3">
            <span class="truncate text-xs font-medium text-highlighted sm:text-sm">{{ facility.name }}</span>
            <span class="truncate text-[10px] text-dimmed sm:text-xs">{{ facility.kind }}</span>
          </div>
          <div
            class="relative h-12 border-t border-default"
            :style="hourLines"
          >
            <div
              v-for="slot in facility.slots"
              :key="slot.label"
              class="absolute inset-y-1.5 flex items-center overflow-hidden rounded-md px-1.5 text-[10px] font-medium ring ring-inset sm:px-2 sm:text-xs"
              :class="tones[slot.tone]"
              :style="place(slot)"
            >
              <span class="hidden truncate sm:inline">{{ slot.label }}</span>
            </div>

            <div
              v-if="facility.picking"
              class="absolute inset-y-1.5 flex items-center overflow-visible rounded-md border-2 border-dashed border-primary bg-primary/10 px-1.5 text-[10px] font-semibold text-primary sm:px-2 sm:text-xs"
              :style="place(facility.picking)"
            >
              <span class="hidden truncate sm:inline">{{ facility.picking.label }}</span>
              <UIcon
                name="i-lucide-mouse-pointer-2"
                class="absolute -right-2.5 -bottom-3 size-5 fill-default text-highlighted"
              />
            </div>

            <div
              class="absolute inset-y-0 w-px bg-primary"
              :style="{ left: along(now) }"
            />
          </div>
        </template>
      </div>
    </div>

    <!-- What happens once something is booked -->
    <div class="absolute -bottom-6 -left-4 hidden w-64 items-start gap-3 rounded-xl bg-default/95 p-3.5 text-left shadow-xl ring ring-default backdrop-blur md:flex lg:-left-10">
      <span class="flex size-8 shrink-0 items-center justify-center rounded-full bg-success/15 text-success">
        <UIcon
          name="i-lucide-check"
          class="size-4"
        />
      </span>
      <div class="min-w-0 text-xs">
        <p class="font-semibold text-highlighted">
          Booking confirmed
        </p>
        <p class="mt-0.5 text-muted">
          Science Lab 2 · 13:30 to 15:00
        </p>
        <p class="mt-1.5 inline-flex items-center gap-1 text-dimmed">
          <UIcon
            name="i-lucide-calendar-sync"
            class="size-3.5"
          />
          Added to Google Calendar
        </p>
      </div>
    </div>

    <div class="absolute -top-6 -right-4 hidden w-60 items-start gap-3 rounded-xl bg-default/95 p-3.5 text-left shadow-xl ring ring-default backdrop-blur md:flex lg:-right-10">
      <span class="flex size-8 shrink-0 items-center justify-center rounded-full bg-info/15 text-info">
        <UIcon
          name="i-simple-icons-telegram"
          class="size-4"
        />
      </span>
      <div class="min-w-0 text-xs">
        <p class="font-semibold text-highlighted">
          Main Hall booked
        </p>
        <p class="mt-0.5 text-muted">
          Town hall, 14:00 to 17:00, by Operations
        </p>
      </div>
    </div>
  </div>
</template>
