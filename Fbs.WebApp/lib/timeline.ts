import { fromDate } from '@internationalized/date'
import { addDaysIn } from './slots'

// Kept out of utils/ and composables/ for the reason given in slots.ts: those are auto-imported, and the timeline of the
// pages that are not organization-aware has names of its own in there.

export const HOUR_WIDTH = 28
export const DAY_WIDTH = HOUR_WIDTH * 24

export interface TimelineFacility {
  id: string
  name: string
  group?: string | null
}

export interface TimelineBooking {
  id: string
  facilityId: string
  facilityName?: string | null
  startDateTime: Date
  endDateTime: Date
}

export interface TimelineBlock<B> {
  booking: B
  /** Pixels from the start of the window */
  left: number
  width: number
}

export interface TimelineRow<B> {
  facilityId: string
  name: string
  blocks: TimelineBlock<B>[]
}

export interface TimelineSection<B> {
  group: string
  rows: TimelineRow<B>[]
}

/** The time on the wall in the time zone, counted as if it were UTC, so that a day is 24 hours whatever the clocks did. */
function wallTime(date: Date, timeZone: string) {
  const z = fromDate(date, timeZone)
  return Date.UTC(z.year, z.month - 1, z.day, z.hour, z.minute, z.second, z.millisecond)
}

/**
 * How far along the timeline a moment is, in pixels. The timeline has a column for each day, and each is 24 hours of
 * the wall clock: when the clocks go back a day is 25 hours long, but ten in the morning is still where ten is.
 */
export function positionOf(date: Date, from: Date, timeZone: string) {
  return ((wallTime(date, timeZone) - wallTime(from, timeZone)) / 3_600_000) * HOUR_WIDTH
}

/** The moment each day of the window begins in. */
export function dayStarts(from: Date, days: number, timeZone: string) {
  return Array.from({ length: days }, (_, i) => addDaysIn(from, i, timeZone))
}

export interface TimelineInput<B> {
  bookings: B[]
  facilities: TimelineFacility[]
  /** The start of the first day */
  from: Date
  days: number
  now: Date
  timeZone: string
  /** Only these facilities, if there are any */
  only?: string[]
  onlyBooked?: boolean
}

/**
 * What the timeline draws: the facilities under the kind they are, each with the bookings that have not finished, as
 * far as the window shows of them. A facility that has gone can still have bookings, and is shown with them.
 */
export function layoutTimeline<B extends TimelineBooking>(input: TimelineInput<B>): TimelineSection<B>[] {
  const { bookings, facilities, from, days, now, timeZone, only = [], onlyBooked = false } = input
  const to = addDaysIn(from, days, timeZone)
  const total = days * DAY_WIDTH

  const rows = new Map<string, TimelineRow<B> & { group: string }>()
  for (const facility of facilities) {
    rows.set(facility.id, { facilityId: facility.id, name: facility.name, group: facility.group ?? 'Other', blocks: [] })
  }

  for (const booking of bookings) {
    const { startDateTime: start, endDateTime: end } = booking
    if (end <= now || end <= from || start >= to) {
      continue
    }

    let row = rows.get(booking.facilityId)
    if (!row) {
      row = { facilityId: booking.facilityId, name: booking.facilityName ?? 'A facility that is gone', group: 'Other', blocks: [] }
      rows.set(booking.facilityId, row)
    }

    const left = Math.max(positionOf(start, from, timeZone), 0)
    const right = Math.min(positionOf(end, from, timeZone), total)
    row.blocks.push({ booking, left, width: right - left })
  }

  const sections = new Map<string, TimelineRow<B>[]>()
  for (const row of rows.values()) {
    if ((only.length && !only.includes(row.facilityId)) || (onlyBooked && row.blocks.length === 0)) {
      continue
    }

    row.blocks.sort((a, b) => a.left - b.left)
    sections.set(row.group, [...(sections.get(row.group) ?? []), row])
  }

  return [...sections].map(([group, groupRows]) => ({ group, rows: groupRows }))
}
