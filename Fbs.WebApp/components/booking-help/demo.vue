<script setup lang="ts">
// A small calendar with a time being dragged out on it, to show how the booking page's is used: two facilities, a booking already on the
// second, and 10:00 to 11:30 dragged out on the first.
const props = defineProps<{
  touch?: boolean
}>()

const hours = ['09:00', '10:00', '11:00', '12:00']
const description = computed(() => props.touch
  ? 'A finger holds on 10:00 in the Hall column, then drags down to 11:30.'
  : 'A pointer presses on 10:00 in the Hall column, and drags down to 11:30.')
</script>

<template>
  <div
    class="demo"
    role="img"
    :aria-label="description"
  >
    <div class="demo__corner" />
    <div class="demo__heading">
      Hall
    </div>
    <div class="demo__heading">
      Gym
    </div>

    <div class="demo__times">
      <span
        v-for="hour in hours"
        :key="hour"
      >{{ hour }}</span>
    </div>
    <div class="demo__column">
      <div class="demo__picked">
        <BookingHelpGesture
          :touch="touch"
          label="10:00 – 11:30"
        />
      </div>
    </div>
    <div class="demo__column">
      <div class="demo__taken">
        Taken
      </div>
    </div>
  </div>
</template>

<style scoped>
.demo {
  --row: 2.5rem;
  display: grid;
  grid-template-columns: 2.75rem 1fr 1fr;
  grid-template-rows: auto calc(var(--row) * 4);
  overflow: hidden;
  border: 1px solid var(--ui-border);
  border-radius: calc(var(--ui-radius) * 2);
  background-color: var(--ui-bg);
  font-size: 0.6875rem;
  user-select: none;
}

.demo__corner,
.demo__heading {
  padding: 0.25rem;
  border-bottom: 1px solid var(--ui-border);
  background-color: var(--ui-bg-elevated);
}

.demo__heading {
  text-align: center;
  font-weight: 600;
  color: var(--ui-text-highlighted);
}

.demo__times {
  display: grid;
  grid-template-rows: repeat(4, var(--row));
  color: var(--ui-text-muted);
}

.demo__times span {
  padding-inline: 0.25rem;
  transform: translateY(-0.4em);
}

.demo__times span:first-child {
  transform: none;
}

/* An hour a row, with a fainter line at each half */
.demo__column {
  position: relative;
  border-inline-start: 1px solid var(--ui-border);
  background-image:
    linear-gradient(to bottom, var(--ui-border) 1px, transparent 1px),
    linear-gradient(to bottom, color-mix(in oklab, var(--ui-border) 45%, transparent) 1px, transparent 1px);
  background-size: 100% var(--row), 100% calc(var(--row) / 2);
}

.demo__picked {
  position: absolute;
  inset-inline: 10%;
  top: var(--row);
  height: calc(var(--row) * 1.5);
}

.demo__taken {
  position: absolute;
  inset-inline: 6%;
  top: calc(var(--row) * 0.5);
  height: calc(var(--row) * 1.5);
  padding: 0.125rem 0.25rem;
  border-radius: calc(var(--ui-radius) * 1.25);
  border-inline-start: 3px solid var(--ui-primary);
  background-color: color-mix(in oklab, var(--ui-primary) 16%, var(--ui-bg));
  color: var(--ui-text-highlighted);
}
</style>
