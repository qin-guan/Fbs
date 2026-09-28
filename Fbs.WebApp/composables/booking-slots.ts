import type { DateFormatter } from '@internationalized/date'
import type { FbsWebApiDtosBookingWithUser } from '~/api/models'

// Keep in sync with the API's batch limit (Endpoints/Booking/Batch/Post/Validator.cs)
export const MAX_BATCH_SLOTS = 50

export interface BookingSlot {
  id: string
  facilityName: string
  start: Date
  end: Date
}

export type NewBookingSlot = Omit<BookingSlot, 'id'>

export interface SlotPlan {
  facilities: string[]
  /** Local midnights of the chosen days */
  days: Date[]
  allDay: boolean
  /** HH:mm */
  from: string
  /** HH:mm, where 24:00 is midnight at the end of the day */
  to: string
  /** One booking per day, rather than one continuous booking for consecutive days */
  split: boolean
}

export type SlotClash
  = | { kind: 'past' }
    | { kind: 'existing', booking: FbsWebApiDtosBookingWithUser }
    | { kind: 'selection', index: number }

interface StoredSlot {
  id: string
  facilityName: string
  start: string
  end: string
}

const DAY_MS = 24 * 60 * 60 * 1000

export function startOfDay(date: Date) {
  const day = new Date(date)
  day.setHours(0, 0, 0, 0)
  return day
}

export function addDays(date: Date, days: number) {
  const result = new Date(date)
  result.setDate(result.getDate() + days)
  return result
}

function atTime(day: Date, time: string) {
  const [hours, minutes] = time.split(':').map(Number)
  const result = startOfDay(day)
  result.setHours(hours ?? 0, minutes ?? 0)
  return result
}

/** The next half hour after now. Bookings must start in the future, on the half hour. */
export function nextHalfHour(now = new Date()) {
  const result = new Date(now)
  result.setSeconds(0, 0)
  result.setMinutes(result.getMinutes() < 30 ? 30 : 60)
  return result
}

/**
 * The whole day, midnight to midnight. If the day has already started, from the next half hour.
 */
export function wholeDay(day: Date, now = new Date()) {
  const start = startOfDay(day)
  const end = addDays(start, 1)
  return { start: start < now ? nextHalfHour(now) : start, end }
}

export function overlaps(a: { start: Date, end: Date }, b: { start: Date, end: Date }) {
  return a.start < b.end && a.end > b.start
}

/** Groups days into runs of consecutive days. */
export function consecutiveRuns(days: Date[]) {
  const sorted = [...new Set(days.map(d => startOfDay(d).getTime()))].sort((a, b) => a - b)
  const runs: Date[][] = []
  for (const time of sorted) {
    const run = runs.at(-1)
    const previous = run?.at(-1)
    if (run && previous && Math.round((time - previous.getTime()) / DAY_MS) === 1) {
      run.push(new Date(time))
    }
    else {
      runs.push([new Date(time)])
    }
  }
  return runs
}

/**
 * Turns the chosen facilities, days and times into slots to book.
 * Slots that would start in the past are left out and counted in `skipped`.
 */
export function planSlots(plan: SlotPlan, now = new Date()) {
  const runs = plan.split
    ? consecutiveRuns(plan.days).flat().map(day => [day])
    : consecutiveRuns(plan.days)

  const slots: NewBookingSlot[] = []
  let skipped = 0

  for (const facilityName of plan.facilities) {
    for (const run of runs) {
      const first = run[0]!
      const last = run.at(-1)!

      let start: Date
      let end: Date
      if (plan.allDay) {
        ({ start } = wholeDay(first, now))
        end = addDays(startOfDay(last), 1)
      }
      else {
        start = atTime(first, plan.from)
        end = atTime(last, plan.to)
      }

      if (start < now || end <= start) {
        skipped++
        continue
      }

      slots.push({ facilityName, start, end })
    }
  }

  return { slots, skipped }
}

/**
 * For each slot, why it can't be booked: it has already started, or it clashes with
 * an existing booking or an earlier slot in the list.
 */
export function findClashes(
  slots: Array<{ facilityName: string, start: Date, end: Date }>,
  bookings: FbsWebApiDtosBookingWithUser[] | undefined,
  now = new Date(),
): Array<SlotClash | undefined> {
  return slots.map((slot, i) => {
    if (slot.start < now) {
      return { kind: 'past' }
    }

    const booking = bookings?.find(b =>
      b.facilityName === slot.facilityName
      && b.startDateTime && b.endDateTime
      && overlaps(slot, { start: b.startDateTime, end: b.endDateTime }),
    )
    if (booking) {
      return { kind: 'existing', booking }
    }

    const index = slots.findIndex((other, j) => j < i && other.facilityName === slot.facilityName && overlaps(slot, other))
    if (index >= 0) {
      return { kind: 'selection', index }
    }

    return undefined
  })
}

/** Human readable date and time for a slot, e.g. "3 Oct 2026" and "All day". */
export function describeSlot(slot: { start: Date, end: Date }, df: DateFormatter, tf: DateFormatter) {
  const startsAtMidnight = slot.start.getTime() === startOfDay(slot.start).getTime()
  const endsAtMidnight = slot.end.getTime() === startOfDay(slot.end).getTime()
  const lastDay = endsAtMidnight ? addDays(slot.end, -1) : slot.end
  const days = Math.round((startOfDay(lastDay).getTime() - startOfDay(slot.start).getTime()) / DAY_MS) + 1

  if (startsAtMidnight && endsAtMidnight) {
    return days === 1
      ? { date: df.format(slot.start), time: 'All day' }
      : { date: `${df.format(slot.start)} – ${df.format(lastDay)}`, time: `All day, ${days} days` }
  }

  if (days === 1) {
    return { date: df.format(slot.start), time: `${tf.format(slot.start)} – ${endsAtMidnight ? 'midnight' : tf.format(slot.end)}` }
  }

  return {
    date: `${df.format(slot.start)} – ${df.format(lastDay)}`,
    time: `${tf.format(slot.start)} – ${endsAtMidnight ? 'midnight' : tf.format(slot.end)}, ${days} days`,
  }
}

function newId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/**
 * Slots picked for booking, kept across pages (and reloads) until they're booked.
 */
export const useBookingBasket = createSharedComposable(() => {
  const stored = useSessionStorage<StoredSlot[]>('booking-basket', [])

  const slots = computed<BookingSlot[]>(() => stored.value.map(s => ({
    id: s.id,
    facilityName: s.facilityName,
    start: new Date(s.start),
    end: new Date(s.end),
  })))

  function add(newSlots: NewBookingSlot[]) {
    stored.value = [
      ...stored.value,
      ...newSlots.map(s => ({ id: newId(), facilityName: s.facilityName, start: s.start.toISOString(), end: s.end.toISOString() })),
    ]
  }

  function remove(ids: string[]) {
    stored.value = stored.value.filter(s => !ids.includes(s.id))
  }

  function clear() {
    stored.value = []
  }

  return { slots, add, remove, clear }
})
