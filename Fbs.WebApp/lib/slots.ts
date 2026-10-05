// Kept out of utils/ and composables/ on purpose: everything in those is auto-imported, and these names
// (planSlots, findClashes, wholeDay ...) are the same as the ones of the pages that are not organization-aware.
import { CalendarDateTime, fromDate, toCalendarDate, toZoned, type CalendarDate, type DateFormatter } from '@internationalized/date'

// Booking slots, worked out in the organization's time zone rather than the browser's: the organization is where the facilities are, and a
// day, or a slot on the half hour, is one there. Times are real moments (a Date), and it is only when they are drawn that they are shifted.

// Keep in sync with the API's limit of slots together (Endpoints/Org/Bookings/Post)
export const MAX_BATCH_SLOTS = 50

export interface Slot {
  facilityId: string
  start: Date
  end: Date
}

export interface BasketSlot extends Slot {
  id: string
}

/** What there is to know of a booking that is there already, to say what a slot clashes with. */
export interface KnownBooking {
  id: string
  facilityId: string
  startDateTime: Date
  endDateTime: Date
  conduct: string
  bookedBy?: { displayName: string }
}

export type SlotClash
  = | { kind: 'past' }
    | { kind: 'existing', booking: KnownBooking }
    | { kind: 'selection', index: number }

export interface Zone {
  timeZone: string
  slotMinutes: number
}

/** A time as a Date whose local fields are the time on the wall in the time zone, which is what a calendar in the browser draws. */
export function toWall(date: Date, timeZone: string): Date {
  const z = fromDate(date, timeZone)
  return new Date(z.year, z.month - 1, z.day, z.hour, z.minute, z.second, z.millisecond)
}

/** The moment that a time on the wall in the time zone, as drawn by a calendar in the browser, is. */
export function fromWall(wall: Date, timeZone: string): Date {
  return toZoned(new CalendarDateTime(wall.getFullYear(), wall.getMonth() + 1, wall.getDate(), wall.getHours(), wall.getMinutes(), wall.getSeconds(), wall.getMilliseconds()), timeZone).toDate()
}

/** The moment the day begins in, for the day the time is in. */
export function startOfDayIn(date: Date, timeZone: string): Date {
  return toZoned(toCalendarDate(fromDate(date, timeZone)), timeZone).toDate()
}

/** A number of days on, at the same time of day. */
export function addDaysIn(date: Date, days: number, timeZone: string): Date {
  return fromDate(date, timeZone).add({ days }).toDate()
}

export function dayOf(date: Date, timeZone: string): CalendarDate {
  return toCalendarDate(fromDate(date, timeZone))
}

/** The next time a slot could start after now: bookings start in the future, on the slot. */
export function nextSlot(now: Date, { timeZone, slotMinutes }: Zone): Date {
  const z = fromDate(now, timeZone)
  const minutes = z.hour * 60 + z.minute
  const next = (Math.floor(minutes / slotMinutes) + 1) * slotMinutes
  return toZoned(new CalendarDateTime(z.year, z.month, z.day).add({ minutes: next }), timeZone).toDate()
}

/** The whole day, midnight to midnight. If the day has already started, from the next slot. */
export function wholeDay(day: Date, now: Date, zone: Zone) {
  const start = startOfDayIn(day, zone.timeZone)
  const end = addDaysIn(start, 1, zone.timeZone)
  return { start: start < now ? nextSlot(now, zone) : start, end }
}

export function overlaps(a: { start: Date, end: Date }, b: { start: Date, end: Date }) {
  return a.start < b.end && a.end > b.start
}

/** Groups days into runs of consecutive days. */
export function consecutiveRuns(days: CalendarDate[]) {
  const sorted = [...days].sort((a, b) => a.compare(b))
  const runs: CalendarDate[][] = []
  for (const day of sorted) {
    const run = runs.at(-1)
    const previous = run?.at(-1)
    if (previous?.compare(day) === 0) {
      continue
    }

    if (run && previous && previous.add({ days: 1 }).compare(day) === 0) {
      run.push(day)
    }
    else {
      runs.push([day])
    }
  }
  return runs
}

export interface SlotPlan {
  facilityIds: string[]
  days: CalendarDate[]
  allDay: boolean
  /** HH:mm */
  from: string
  /** HH:mm, where 24:00 is midnight at the end of the day */
  to: string
  /** One booking per day, rather than one continuous booking for consecutive days */
  split: boolean
}

function atTime(day: CalendarDate, time: string, timeZone: string) {
  const [hours, minutes] = time.split(':').map(Number)
  return toZoned(new CalendarDateTime(day.year, day.month, day.day).add({ hours: hours ?? 0, minutes: minutes ?? 0 }), timeZone).toDate()
}

/**
 * Turns the chosen facilities, days and times into slots to book.
 * Slots that would start in the past are left out and counted in `skipped`.
 */
export function planSlots(plan: SlotPlan, now: Date, zone: Zone) {
  const runs = plan.split ? consecutiveRuns(plan.days).flat().map(day => [day]) : consecutiveRuns(plan.days)

  const slots: Slot[] = []
  let skipped = 0

  for (const facilityId of plan.facilityIds) {
    for (const run of runs) {
      const first = run[0]!
      const last = run.at(-1)!

      let start: Date
      let end: Date
      if (plan.allDay) {
        ;({ start } = wholeDay(atTime(first, '00:00', zone.timeZone), now, zone))
        end = atTime(last.add({ days: 1 }), '00:00', zone.timeZone)
      }
      else {
        start = atTime(first, plan.from, zone.timeZone)
        end = atTime(last, plan.to, zone.timeZone)
      }

      if (start < now || end <= start) {
        skipped++
        continue
      }

      slots.push({ facilityId, start, end })
    }
  }

  return { slots, skipped }
}

/**
 * For each slot, why it can't be booked: it has already started, or it clashes with
 * a booking that is there or an earlier slot in the list.
 */
export function findClashes(slots: Slot[], bookings: KnownBooking[] | undefined, now: Date): Array<SlotClash | undefined> {
  return slots.map((slot, i) => {
    if (slot.start < now) {
      return { kind: 'past' }
    }

    const booking = bookings?.find(b => b.facilityId === slot.facilityId && overlaps(slot, { start: b.startDateTime, end: b.endDateTime }))
    if (booking) {
      return { kind: 'existing', booking }
    }

    const index = slots.findIndex((other, j) => j < i && other.facilityId === slot.facilityId && overlaps(slot, other))
    return index >= 0 ? { kind: 'selection', index } : undefined
  })
}

/** Human readable date and time for a slot, e.g. "3 Oct 2026" and "All day". */
export function describeSlot(slot: { start: Date, end: Date }, df: DateFormatter, tf: DateFormatter, timeZone: string) {
  const startDay = startOfDayIn(slot.start, timeZone)
  const endDay = startOfDayIn(slot.end, timeZone)
  const startsAtMidnight = slot.start.getTime() === startDay.getTime()
  const endsAtMidnight = slot.end.getTime() === endDay.getTime()
  const lastDay = endsAtMidnight ? addDaysIn(slot.end, -1, timeZone) : slot.end
  const days = dayOf(lastDay, timeZone).compare(dayOf(slot.start, timeZone)) + 1

  const until = endsAtMidnight ? 'midnight' : tf.format(slot.end)
  if (startsAtMidnight && endsAtMidnight) {
    return days === 1
      ? { date: df.format(slot.start), time: 'All day' }
      : { date: `${df.format(slot.start)} – ${df.format(lastDay)}`, time: `All day, ${days} days` }
  }

  if (days === 1) {
    return { date: df.format(slot.start), time: `${tf.format(slot.start)} – ${until}` }
  }

  return { date: `${df.format(slot.start)} – ${df.format(lastDay)}`, time: `${tf.format(slot.start)} – ${until}, ${days} days` }
}

export type RescheduleProblem
  = | { kind: 'over' }
    | { kind: 'started' }
    | { kind: 'past' }
    | { kind: 'order' }
    | { kind: 'existing', booking: KnownBooking }

/**
 * Why a booking can't be moved to a new time, mirroring the API's checks: one that is over can't move, one underway keeps its
 * start, and the new time can't clash with another booking.
 */
export function findRescheduleProblem(
  booking: { id: string, facilityId: string, start: Date, end: Date },
  slot: { start: Date, end: Date },
  bookings: KnownBooking[] | undefined,
  now: Date,
): RescheduleProblem | undefined {
  if (booking.end <= now) {
    return { kind: 'over' }
  }

  if (slot.start.getTime() !== booking.start.getTime()) {
    if (booking.start <= now) {
      return { kind: 'started' }
    }

    if (slot.start < now) {
      return { kind: 'past' }
    }
  }

  if (slot.end <= slot.start) {
    return { kind: 'order' }
  }

  if (slot.end <= now) {
    return { kind: 'past' }
  }

  const clash = bookings?.find(b => b.id !== booking.id && b.facilityId === booking.facilityId && overlaps(slot, { start: b.startDateTime, end: b.endDateTime }))
  return clash ? { kind: 'existing', booking: clash } : undefined
}
