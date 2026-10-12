import { driver, type Driver, type DriveStep } from 'driver.js'
import { classifyPress, IDLE_MS, MIN_DRAG_PX, NEWCOMER_RULE, noteMiss, REGULAR_RULE, type PressKind } from '~/lib/struggle'

// Helping people learn the booking page: a walkthrough of it, and an offer of help when they look stuck picking a time. Both builds' booking
// pages use it, so it only asks of a page what both have: the ids below, and vue-cal's own classes.

/** What the browser remembers of somebody learning the booking page. It is the same calendar in both builds, so it is kept once for both. */
export function useBookingHelpMemory() {
  return {
    /** They finished or closed the walkthrough */
    toured: useLocalStorage('fbs:booking-tour-done', false),
    /** They have picked a time on the calendar at least once, so they know it is dragged out */
    picked: useLocalStorage('fbs:picked-a-time', false),
  }
}

/** Why help with picking a time is offered: a press that picked nothing, of that kind, or a newcomer who hasn't picked one for a while. */
export type BookingHint = Exclude<PressKind, 'scroll'> | 'idle'

const howToPick = {
  mouse: 'Press where it starts, in the facility\'s column, and <strong>drag down</strong> to where it ends. Try it now.',
  touch: 'Touch where it starts, in the facility\'s column, and <strong>hold for half a second</strong>. Then drag down to where it ends. Try it now. A quick swipe scrolls the calendar instead.',
}

/** What to tell somebody who is offered help, by why. */
export function bookingHintText(hint: BookingHint, touch: boolean) {
  switch (hint) {
    case 'tap':
      return touch
        ? 'A tap doesn\'t pick a time. Keep your finger down for half a second, then drag down to where it ends.'
        : 'A click doesn\'t pick a time. Keep the button down and drag down to where it ends.'
    case 'hold':
      return 'Almost. After holding, drag down to where it ends before you let go.'
    case 'drag':
      return 'Nothing was picked. Start on an empty part of a facility\'s column, and drag down to where it ends.'
    case 'idle':
      return touch
        ? 'Touch where it starts in the facility\'s column and hold for half a second. Then drag down to where it ends.'
        : 'Press where it starts in the facility\'s column, and drag down to where it ends.'
  }
}

export type BookingTourStep = 'facility' | 'day' | 'pick' | 'adjust' | 'summary' | 'multiple' | 'confirm'

/**
 * The steps where the calendar is used, not only looked at. driver.js swallows a press that ends on its overlay, which a drag can easily do,
 * and vue-cal would never hear that it ended: while these are on, presses go through the overlay (see main.css).
 */
const handsOn = new Set<BookingTourStep>(['pick', 'adjust'])

/**
 * A walkthrough of the booking page, with driver.js. At "pick" they are asked to drag out a time on the real calendar, and it goes on by
 * itself once they have: nothing is booked until the confirm page, so practising on it costs nothing.
 *
 * The page has `#facility-type`, `#booking-calendar` (around vue-cal), `#booking-summary`, `#book-multiple` and `#confirm-selection`.
 */
export function useBookingTour(options: {
  /** The time picked on the calendar, if there is one */
  selection: Ref<unknown>
  /** To speak of a finger rather than a mouse */
  touch: Ref<boolean>
}) {
  const { toured } = useBookingHelpMemory()
  const selected = computed(() => !!options.selection.value)
  const step = ref<BookingTourStep>()
  let tour: Driver | undefined
  let ids: BookingTourStep[] = []

  const indexOf = (id: BookingTourStep) => ids.indexOf(id)
  const calendar = () => document.querySelector('#booking-calendar') ?? document.body
  // While it is redrawn, vue-cal can have the time picked twice for a moment, one of them not laid out
  const picked = () => [...document.querySelectorAll('.vuecal__event.new-booking')].find(event => event.getBoundingClientRect().height > 0)

  function steps(): [BookingTourStep, DriveStep][] {
    const touch = options.touch.value
    return [
      ['facility', {
        element: '#facility-type',
        // Its menu opens in a layer the overlay would cover, so it is only pointed at
        disableActiveInteraction: true,
        popover: { title: 'Facility types', description: 'Facilities are grouped by type. Pick one here, and each facility of that type gets a column on the calendar.' },
      }],
      ['day', {
        element: '.vuecal__header',
        popover: { title: 'Pick a day', description: 'Go to another day with the arrows, or press <strong>Week</strong> to see seven at once.' },
      }],
      ['pick', {
        element: calendar,
        popover: {
          // Off to the side of the calendar, rather than over the middle of it where the time is to be picked
          popoverClass: 'booking-tour-aside',
          title: 'Now pick a time',
          description: touch ? howToPick.touch : howToPick.mouse,
          // It goes on by itself when a time is picked. Until then the button skips what is about the time picked.
          onPopoverRender: (popover) => {
            popover.nextButton.innerHTML = selected.value ? 'Next &rarr;' : 'Skip'
          },
          onNextClick: () => selected.value ? tour?.moveNext() : tour?.moveTo(indexOf('multiple')),
        },
      }],
      ['adjust', {
        element: () => picked() ?? calendar(),
        popover: {
          title: 'Change it if you need to',
          description: touch
            ? 'Hold it for a moment, then drag to move it. Drag the bar along its bottom to change when it ends.'
            : 'Drag it to move it. Drag the bar along its bottom to change when it ends.',
        },
      }],
      ['summary', {
        element: '#booking-summary',
        popover: {
          title: 'Check what you picked',
          description: 'The facility, day and time show here. <strong>All day</strong> stretches it to the whole day. <strong>Add to list</strong> keeps it, so you can pick another and book them together.',
        },
      }],
      ['multiple', {
        element: '#book-multiple',
        popover: {
          title: 'Several at once',
          description: 'For more than one facility or day, or to choose the times from a list instead of dragging, use <strong>Book multiple</strong>.',
          // Back past what is about the time picked, if none was
          onPrevClick: () => selected.value ? tour?.movePrevious() : tour?.moveTo(indexOf('pick')),
        },
      }],
      ['confirm', {
        element: '#confirm-selection',
        popover: { title: 'Then carry on', description: 'Next you say what it\'s for and who to contact. Nothing is booked until you confirm there.' },
      }],
    ]
  }

  function start(from: BookingTourStep = 'facility') {
    tour?.destroy()

    const list = steps()
    ids = list.map(([id]) => id)
    tour = driver({
      showProgress: true,
      stagePadding: 6,
      steps: list.map(([, s]) => s),
      onHighlightStarted: () => {
        step.value = ids[tour?.getActiveIndex() ?? -1]
        document.body.classList.toggle('booking-tour-hands-on', !!step.value && handsOn.has(step.value))
      },
      onDestroyed: () => {
        document.body.classList.remove('booking-tour-hands-on')
        step.value = undefined
        toured.value = true
        tour = undefined
      },
    })
    tour.drive(Math.max(indexOf(from), 0))
  }

  /** Says, under how to pick a time, why the last press picked nothing. */
  function explain(hint: BookingHint) {
    const description = tour?.getState('popover')?.description as HTMLElement | undefined
    if (step.value !== 'pick' || !description) {
      return
    }

    const touch = options.touch.value
    description.innerHTML = `${touch ? howToPick.touch : howToPick.mouse}<span class="booking-tour-tip">${bookingHintText(hint, touch)}</span>`
    tour?.refresh()
  }

  // On from "pick" when a time is picked, once vue-cal has drawn it; and when it is moved or stretched, it is still what is pointed at
  watch(options.selection, () => {
    setTimeout(() => {
      if (step.value === 'pick' && selected.value) {
        tour?.moveNext()
      }
      else if (step.value === 'adjust') {
        // vue-cal may draw it as another element, which is then pointed at instead. (It also takes off what driver.js marks it with, so what
        // lets it still be pressed is in main.css.)
        if (picked() === tour?.getActiveElement()) {
          tour?.refresh()
        }
        else {
          tour?.moveTo(indexOf('adjust'))
        }
      }
    }, 200)
  }, { flush: 'post' })

  onBeforeUnmount(() => tour?.destroy())

  return { start, step, explain, toured }
}

/**
 * Watches presses on the empty parts of the calendar, and says when somebody looks stuck picking a time: their presses keep picking nothing,
 * or they have never picked one and haven't for a while. A press is only counted once vue-cal has had the chance to make a time of it.
 */
export function useBookingStruggle(calendar: MaybeRefOrGetter<HTMLElement | null | undefined>, options: {
  /** What is picked: any change to it is a press that worked */
  selection: Ref<unknown>
  /** Whether to offer help at all, such as when there is something to book and no walkthrough is on */
  enabled: Ref<boolean>
  /** Whether nothing has been picked or listed yet, so a newcomer who waits may be stuck */
  empty: Ref<boolean>
  /** Set to how the last press was made: `mouse`, `touch` or `pen` */
  pointerType: Ref<string | undefined>
  /** Told of every press that picked nothing, whether or not help is offered for it */
  onMiss?: (hint: BookingHint) => void
}) {
  const { picked } = useBookingHelpMemory()
  const hint = ref<BookingHint>()

  let misses: number[] = []
  let changes = 0
  watch(options.selection, (value) => {
    changes++
    if (value) {
      picked.value = true
      hint.value = undefined
      misses = []
    }
  })

  let press: { id: number, x: number, y: number, at: number, pointerType: string, movedAt?: number, distance: number, changes: number } | undefined

  useEventListener(calendar, 'pointerdown', (event: PointerEvent) => {
    const target = event.target as Element | null
    // Only presses on empty time: a booking is opened with a double click, and the time picked is moved and stretched
    if (!event.isPrimary || event.button !== 0 || !target?.closest('.vuecal__cell') || target.closest('.vuecal__event')) {
      return
    }

    press = { id: event.pointerId, x: event.clientX, y: event.clientY, at: event.timeStamp, pointerType: event.pointerType, distance: 0, changes }
  }, { capture: true })

  useEventListener(window, 'pointermove', (event: PointerEvent) => {
    if (press?.id !== event.pointerId) {
      return
    }

    const distance = Math.hypot(event.clientX - press.x, event.clientY - press.y)
    press.distance = Math.max(press.distance, distance)
    if (press.movedAt === undefined && distance > MIN_DRAG_PX) {
      press.movedAt = event.timeStamp
    }
  }, { passive: true })

  function finish(event: PointerEvent, cancelled: boolean) {
    const ended = press
    if (ended?.id !== event.pointerId) {
      return
    }

    press = undefined
    const kind = classifyPress({ pointerType: ended.pointerType, heldMs: (ended.movedAt ?? event.timeStamp) - ended.at, distance: ended.distance, cancelled })
    if (kind === 'scroll') {
      return
    }

    options.pointerType.value = ended.pointerType
    // vue-cal hands over the time it made after the press is over, so whether it made one is looked at a little later
    setTimeout(() => {
      if (changes !== ended.changes) {
        return
      }

      options.onMiss?.(kind)
      if (!options.enabled.value) {
        return
      }

      const noted = noteMiss(misses, Date.now(), picked.value ? REGULAR_RULE : NEWCOMER_RULE)
      misses = noted.misses
      if (noted.struggling) {
        hint.value = kind
        misses = []
      }
    }, 300)
  }

  useEventListener(window, 'pointerup', (event: PointerEvent) => finish(event, false))
  useEventListener(window, 'pointercancel', (event: PointerEvent) => finish(event, true))

  // Somebody who has never picked a time, and doesn't for a while, is asked once a visit
  let askedIdle = false
  const mayBeIdle = computed(() => options.enabled.value && options.empty.value && !picked.value && !hint.value && !askedIdle)
  const idle = useTimeoutFn(() => {
    if (mayBeIdle.value) {
      askedIdle = true
      hint.value = 'idle'
    }
  }, IDLE_MS, { immediate: false })
  watch(mayBeIdle, may => may ? idle.start() : idle.stop(), { immediate: true })

  function dismiss() {
    hint.value = undefined
    misses = []
  }

  // Help that is no longer wanted goes, such as when the walkthrough starts
  watch(options.enabled, (enabled) => {
    if (!enabled) {
      dismiss()
    }
  })

  return { hint, dismiss }
}

/**
 * The walkthrough and the offer of help together, as a booking page has them. The walkthrough starts by itself the first time, once the
 * calendar is `ready`, or when the page that led here asked for it, with `state: { tour: true }` in where it went.
 */
export function useBookingHelp(calendar: MaybeRefOrGetter<HTMLElement | null | undefined>, options: {
  selection: Ref<unknown>
  /** The calendar is drawn, with something on it to book */
  ready: Ref<boolean>
  /** Nothing is picked or listed yet */
  empty: Ref<boolean>
  /** Whether the walkthrough starts by itself on a first visit */
  firstVisit?: Ref<boolean>
}) {
  const pointerType = ref<string>()
  const coarse = useMediaQuery('(pointer: coarse)')
  const touch = computed(() => pointerType.value ? pointerType.value === 'touch' : coarse.value)

  const tour = useBookingTour({ selection: options.selection, touch })
  const struggle = useBookingStruggle(calendar, {
    selection: options.selection,
    enabled: computed(() => options.ready.value && !tour.step.value),
    empty: options.empty,
    pointerType,
    onMiss: tour.explain,
  })

  // Asked for in the history entry rather than the address, so that it isn't asked for again by going back to the page or reloading it, and
  // nothing has to be taken out of the address while the page is putting its own things in it
  const asked = !!window.history.state?.tour
  whenever(options.ready, () => {
    if (asked || ((options.firstVisit?.value ?? true) && !tour.toured.value)) {
      window.history.replaceState({ ...window.history.state, tour: undefined }, '')
      tour.start()
    }
  }, { immediate: true, once: true })

  /** Offered help taken up: the walkthrough, from picking a time. */
  function showMe() {
    struggle.dismiss()
    tour.start('pick')
  }

  return { touch, hint: struggle.hint, dismiss: struggle.dismiss, tourStep: tour.step, startTour: tour.start, showMe }
}
