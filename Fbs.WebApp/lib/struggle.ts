// Whether somebody is having trouble picking a time on the booking calendar, from how they press on it. It has no Nuxt in it, so it can
// be tried without a browser (tests/struggle.test.ts); composables/booking-help.ts listens to the calendar and asks it.

/** How long a finger has to stay down before vue-cal lets it drag out a time, rather than scroll the calendar. */
export const TOUCH_HOLD_MS = 500

/** How far a press has to move before vue-cal starts a time: `eventCreateMinDrag`, as the booking pages set it. */
export const MIN_DRAG_PX = 20

/** One press on an empty part of the calendar, from going down to coming up. */
export interface Press {
  pointerType: string
  /** How long it was held before it moved further than `MIN_DRAG_PX`, or how long it was down if it never did */
  heldMs: number
  /** The furthest it got from where it went down, in pixels */
  distance: number
  /** The browser took it over, to scroll */
  cancelled: boolean
}

/**
 * What a press would have done:
 * - `tap`: went down and up in place, which picks nothing
 * - `hold`: a finger held long enough, but let go without dragging
 * - `drag`: dragged far enough to pick a time
 * - `scroll`: a finger moved before it was held, which scrolls the calendar
 */
export type PressKind = 'tap' | 'hold' | 'drag' | 'scroll'

export function classifyPress(press: Press): PressKind {
  if (press.cancelled) {
    return 'scroll'
  }

  const moved = press.distance > MIN_DRAG_PX
  if (press.pointerType !== 'touch') {
    return moved ? 'drag' : 'tap'
  }

  if (moved) {
    return press.heldMs >= TOUCH_HOLD_MS ? 'drag' : 'scroll'
  }

  return press.heldMs >= TOUCH_HOLD_MS ? 'hold' : 'tap'
}

/** How long somebody who has never picked a time can be on the booking page without picking one, before they are offered help. */
export const IDLE_MS = 45_000

/** How many presses that picked nothing, and in how long, mean somebody is stuck. */
export interface StruggleRule {
  misses: number
  withinMs: number
}

/** For somebody who has never picked a time here, two misses are enough; they may not know a drag is wanted at all. */
export const NEWCOMER_RULE: StruggleRule = { misses: 2, withinMs: 60_000 }
export const REGULAR_RULE: StruggleRule = { misses: 3, withinMs: 30_000 }

/** Adds a miss at `at` to the ones before it, forgetting those too old to count, and says whether that is now too many. */
export function noteMiss(misses: readonly number[], at: number, rule: StruggleRule) {
  const recent = [...misses.filter(time => at - time <= rule.withinMs), at]
  return { misses: recent, struggling: recent.length >= rule.misses }
}
