<script setup lang="ts">
definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'How to book' })

// How the organization's pages are used, for whoever is new to them, with the walkthrough of the booking page a press away
const { org, timeZone, path } = useTenant()

// Shown for what they are on, and either can be looked at
const touch = ref(useMediaQuery('(pointer: coarse)').value)
function showFor(finger: boolean) {
  touch.value = finger
}
const walkthrough = computed(() => ({ path: path('book'), state: { tour: true } }))

const afterwards = computed(() => [
  {
    title: 'Bookings',
    description: 'Every booking here, the latest first. Turn on Only mine to see just yours.',
    icon: 'i-lucide-calendar-days',
    to: path(),
  },
  {
    title: 'Timeline',
    description: 'A row for each facility over a few days, to see when they are free.',
    icon: 'i-lucide-chart-gantt',
    to: path('timeline'),
  },
  {
    title: 'Changing or cancelling',
    description: 'Open a booking from the list, or double-click it on the calendar. You can change or cancel yours, and your unit\'s.',
    icon: 'i-lucide-pencil',
    to: path(),
  },
  {
    title: 'Telegram',
    description: 'Hear about bookings there. Connect it from your account.',
    icon: 'i-simple-icons-telegram',
    to: '/account',
  },
])
</script>

<template>
  <UDashboardPanel id="guide">
    <template #header>
      <AppNavbar title="How to book" />
    </template>

    <template #body>
      <div class="mx-auto w-full max-w-3xl space-y-8">
        <UPageCard
          :title="`Booking at ${org?.name ?? 'your organization'}`"
          description="It takes a minute. This is how it goes, or try it for real: nothing is booked until you confirm, so you can't get it wrong."
          variant="subtle"
        >
          <div class="flex flex-wrap gap-2">
            <UButton
              :to="walkthrough"
              label="Walk me through it"
              icon="i-lucide-route"
            />
            <UButton
              :to="path('book')"
              label="Make a booking"
              color="neutral"
              variant="outline"
            />
          </div>
        </UPageCard>

        <ol
          class="space-y-6"
          aria-label="How to book"
        >
          <li class="step">
            <span class="step__number">1</span>
            <div class="min-w-0 flex-1">
              <h2 class="step__title">
                Pick a facility type
              </h2>
              <p class="step__text">
                Open <strong>New booking</strong> and choose a type at the top. Each facility of that type gets a column on the calendar.
              </p>
            </div>
          </li>

          <li class="step">
            <span class="step__number">2</span>
            <div class="min-w-0 flex-1">
              <h2 class="step__title">
                Go to the day
              </h2>
              <p class="step__text">
                Use the arrows above the calendar, or <strong>Week</strong> to see seven days at once. Times are in {{ timeZone }}, where
                {{ org?.name ?? 'the organization' }} is.
              </p>
            </div>
          </li>

          <li class="step">
            <span class="step__number">3</span>
            <div class="min-w-0 flex-1 space-y-3">
              <div>
                <h2 class="step__title">
                  Drag out the time
                </h2>
                <p
                  v-if="touch"
                  class="step__text"
                >
                  Touch where it starts, in the facility's column, and <strong>hold for half a second</strong>. Then drag down to where it ends,
                  and let go. A quick swipe scrolls the calendar instead, and a tap on its own doesn't pick anything.
                </p>
                <p
                  v-else
                  class="step__text"
                >
                  Press where it starts, in the facility's column, <strong>keep the button down</strong>, and drag down to where it ends. A click
                  on its own doesn't pick anything.
                </p>
              </div>

              <div class="flex flex-col gap-3 sm:flex-row sm:items-start">
                <BookingHelpDemo
                  :touch="touch"
                  class="w-full max-w-64"
                />
                <div class="space-y-2">
                  <UFieldGroup size="xs">
                    <UButton
                      label="Mouse"
                      icon="i-lucide-mouse"
                      color="neutral"
                      :variant="touch ? 'outline' : 'solid'"
                      :aria-pressed="!touch"
                      @click="showFor(false)"
                    />
                    <UButton
                      label="Touch screen"
                      icon="i-lucide-smartphone"
                      color="neutral"
                      :variant="touch ? 'solid' : 'outline'"
                      :aria-pressed="touch"
                      @click="showFor(true)"
                    />
                  </UFieldGroup>
                  <p class="text-sm text-muted">
                    Can't drag? <strong>Book multiple</strong> lets you choose the times from a list instead.
                  </p>
                </div>
              </div>
            </div>
          </li>

          <li class="step">
            <span class="step__number">4</span>
            <div class="min-w-0 flex-1">
              <h2 class="step__title">
                Adjust it
              </h2>
              <p class="step__text">
                Drag the time to move it{{ touch ? ', after holding it for a moment' : '' }}. Drag the bar along its bottom to change when it ends.
                <strong>All day</strong> stretches it from midnight to midnight.
              </p>
            </div>
          </li>

          <li class="step">
            <span class="step__number">5</span>
            <div class="min-w-0 flex-1">
              <h2 class="step__title">
                More than one? Make a list
              </h2>
              <p class="step__text">
                <strong>Add to list</strong> keeps what you picked, so you can pick another. <strong>Book multiple</strong> does several facilities
                or days at once. Everything in the list is booked together, with one conduct and one point of contact.
              </p>
            </div>
          </li>

          <li class="step">
            <span class="step__number">6</span>
            <div class="min-w-0 flex-1">
              <h2 class="step__title">
                Confirm
              </h2>
              <p class="step__text">
                Say what it's for and who to contact, then press <strong>Confirm</strong>. If anything in your list clashes with another booking,
                nothing is booked: take out what clashes and try again.
              </p>
            </div>
          </li>
        </ol>

        <section
          aria-labelledby="guide-afterwards"
          class="space-y-3"
        >
          <h2
            id="guide-afterwards"
            class="text-base font-semibold text-highlighted"
          >
            Once you've booked
          </h2>
          <div class="grid gap-3 sm:grid-cols-2">
            <UPageCard
              v-for="item in afterwards"
              :key="item.title"
              :title="item.title"
              :description="item.description"
              :icon="item.icon"
              :to="item.to"
              variant="outline"
            />
          </div>
        </section>

        <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-default p-4">
          <p class="text-sm text-muted">
            Ready? The walkthrough asks you to pick a time on the real calendar.
          </p>
          <UButton
            :to="walkthrough"
            label="Walk me through it"
            icon="i-lucide-route"
          />
        </div>
      </div>
    </template>
  </UDashboardPanel>
</template>

<style scoped>
.step {
  display: flex;
  gap: 0.875rem;
}

.step__number {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 1.75rem;
  height: 1.75rem;
  border-radius: 9999px;
  background-color: color-mix(in oklab, var(--ui-primary) 14%, transparent);
  color: var(--ui-primary);
  font-size: 0.875rem;
  font-weight: 600;
}

.step__title {
  font-weight: 600;
  color: var(--ui-text-highlighted);
}

.step__text {
  font-size: 0.875rem;
  color: var(--ui-text-muted);
}

.step__text strong {
  color: var(--ui-text-highlighted);
}
</style>
