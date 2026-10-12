<script setup lang="ts">
// A time being dragged out, over and over: a pointer presses at the top of whatever it is put in, and drags down to the bottom while the
// time grows behind it. With `touch`, a finger holds first, as vue-cal waits for that on a touch screen. It fills its parent, which should be
// positioned, and is only decoration: whatever it is in says what it shows.
defineProps<{
  touch?: boolean
  /** Shown in the time once it has been dragged out, such as `10:00 – 11:30` */
  label?: string
}>()
</script>

<template>
  <div
    class="gesture"
    :class="touch ? 'gesture--touch' : 'gesture--mouse'"
    aria-hidden="true"
  >
    <div class="gesture__block">
      <span
        v-if="label"
        class="gesture__label"
      >{{ label }}</span>
    </div>
    <div class="gesture__pointer">
      <span
        v-if="touch"
        class="gesture__hold"
      />
      <UIcon
        :name="touch ? 'i-lucide-pointer' : 'i-lucide-mouse-pointer-2'"
        class="gesture__icon"
      />
    </div>
  </div>
</template>

<style scoped>
.gesture {
  --gesture-duration: 3.6s;
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.gesture__block {
  position: absolute;
  inset-inline: 0;
  top: 0;
  height: 100%;
  overflow: hidden;
  border-radius: calc(var(--ui-radius) * 1.25);
  border-bottom: 4px solid color-mix(in oklab, var(--ui-primary) 70%, black);
  background-color: var(--ui-primary);
  box-shadow: 0 4px 12px -4px color-mix(in oklab, var(--ui-primary) 60%, transparent);
  animation: var(--gesture-duration) ease-in-out infinite;
}

.gesture__label {
  display: block;
  padding: 0.125rem 0.25rem;
  font-size: 0.625rem;
  font-weight: 600;
  line-height: 1.2;
  white-space: nowrap;
  color: var(--ui-text-inverted);
  animation: gesture-label var(--gesture-duration) ease-in-out infinite;
}

/* The tip of the pointer is where the time ends */
.gesture__pointer {
  position: absolute;
  left: 50%;
  top: 100%;
  width: 0;
  height: 0;
  animation: var(--gesture-duration) ease-in-out infinite;
}

.gesture__icon {
  position: absolute;
  width: 1.375rem;
  height: 1.375rem;
  color: var(--ui-text-highlighted);
  filter: drop-shadow(0 0 1.5px var(--ui-bg)) drop-shadow(0 0 1.5px var(--ui-bg));
}

.gesture--mouse .gesture__icon {
  left: -0.2rem;
  top: -0.15rem;
}

/* The finger of the hand points up, a little left of its middle */
.gesture--touch .gesture__icon {
  left: -0.55rem;
  top: -0.1rem;
}

.gesture__hold {
  position: absolute;
  left: -0.875rem;
  top: -0.875rem;
  width: 1.75rem;
  height: 1.75rem;
  border-radius: 9999px;
  border: 2px solid var(--ui-primary);
  background-color: color-mix(in oklab, var(--ui-primary) 25%, transparent);
  animation: gesture-hold var(--gesture-duration) ease-out infinite;
}

.gesture--mouse .gesture__block { animation-name: gesture-mouse-block; }
.gesture--mouse .gesture__pointer { animation-name: gesture-mouse-pointer; }
.gesture--touch .gesture__block { animation-name: gesture-touch-block; }
.gesture--touch .gesture__pointer { animation-name: gesture-touch-pointer; }

/* Comes in, presses, drags down, lets go, and the time stays a moment */
@keyframes gesture-mouse-pointer {
  0% { top: 0; opacity: 0; transform: translate(12px, 10px); }
  12% { top: 0; opacity: 1; transform: none; }
  18% { top: 0; transform: scale(0.85); }
  62% { top: 100%; transform: scale(0.85); }
  68% { top: 100%; transform: none; }
  90% { top: 100%; opacity: 1; }
  100% { top: 100%; opacity: 0; }
}

@keyframes gesture-mouse-block {
  0%, 18% { height: 0; opacity: 0; }
  20% { height: 6%; opacity: 1; }
  62% { height: 100%; }
  90% { height: 100%; opacity: 1; }
  100% { height: 100%; opacity: 0; }
}

/* A finger holds for a while first, shown by the ring filling, and only then drags */
@keyframes gesture-touch-pointer {
  0% { top: 0; opacity: 0; transform: translate(12px, 10px); }
  10% { top: 0; opacity: 1; transform: none; }
  14% { top: 0; transform: scale(0.85); }
  32% { top: 0; transform: scale(0.85); }
  66% { top: 100%; transform: scale(0.85); }
  72% { top: 100%; transform: none; }
  90% { top: 100%; opacity: 1; }
  100% { top: 100%; opacity: 0; }
}

@keyframes gesture-touch-block {
  0%, 32% { height: 0; opacity: 0; }
  34% { height: 6%; opacity: 1; }
  66% { height: 100%; }
  90% { height: 100%; opacity: 1; }
  100% { height: 100%; opacity: 0; }
}

@keyframes gesture-hold {
  0%, 14% { opacity: 0; transform: scale(0.3); }
  16% { opacity: 1; transform: scale(0.3); }
  32% { opacity: 1; transform: scale(1); }
  38%, 100% { opacity: 0; transform: scale(1.4); }
}

@keyframes gesture-label {
  0%, 62% { opacity: 0; }
  72%, 90% { opacity: 1; }
  100% { opacity: 0; }
}

/* Without motion, what it ends with: the time dragged out, and the pointer where it was let go */
@media (prefers-reduced-motion: reduce) {
  .gesture .gesture__block,
  .gesture .gesture__pointer,
  .gesture .gesture__label {
    animation: none;
  }

  .gesture__hold {
    display: none;
  }
}
</style>
