import { CalendarDate, DateFormatter } from '@internationalized/date'
import { describe, expect, it } from 'vitest'
import { addDaysIn, consecutiveRuns, describeSlot, findClashes, findRescheduleProblem, fromWall, nextSlot, planSlots, startOfDayIn, toWall, wholeDay, type KnownBooking } from '../lib/slots'

const singapore = { timeZone: 'Asia/Singapore', slotMinutes: 30 }
const newYork = { timeZone: 'America/New_York', slotMinutes: 30 }
const utc = (iso: string) => new Date(iso)
const iso = (d: Date) => d.toISOString()

describe('the organization\'s wall clock', () => {
  it('is turned into a time and back, in a zone with no clocks change', () => {
    const moment = utc('2026-10-05T01:00:00Z')

    const wall = toWall(moment, 'Asia/Singapore')

    expect([wall.getFullYear(), wall.getMonth(), wall.getDate(), wall.getHours(), wall.getMinutes()]).toEqual([2026, 9, 5, 9, 0])
    expect(iso(fromWall(wall, 'Asia/Singapore'))).toBe('2026-10-05T01:00:00.000Z')
  })

  it('is the same when the browser is somewhere else', () => {
    // Nine in the morning in New York, in summer time, is 13:00 UTC
    expect(iso(fromWall(new Date(2026, 6, 1, 9, 0), 'America/New_York'))).toBe('2026-07-01T13:00:00.000Z')
    expect(toWall(utc('2026-07-01T13:00:00Z'), 'America/New_York').getHours()).toBe(9)
  })

  it('makes a day of 23 hours when the clocks go forward, and of 25 when they go back', () => {
    const springForward = startOfDayIn(utc('2026-03-08T18:00:00Z'), 'America/New_York')
    const fallBack = startOfDayIn(utc('2026-11-01T18:00:00Z'), 'America/New_York')

    expect((addDaysIn(springForward, 1, 'America/New_York').getTime() - springForward.getTime()) / 3_600_000).toBe(23)
    expect((addDaysIn(fallBack, 1, 'America/New_York').getTime() - fallBack.getTime()) / 3_600_000).toBe(25)
  })
})

describe('the next slot', () => {
  it.each([
    ['2026-10-05T01:10:00Z', '2026-10-05T01:30:00.000Z'], // 09:10 -> 09:30
    ['2026-10-05T01:30:00Z', '2026-10-05T02:00:00.000Z'], // on the slot, so the one after
    ['2026-10-05T01:29:59Z', '2026-10-05T01:30:00.000Z'],
    ['2026-10-05T15:45:00Z', '2026-10-05T16:00:00.000Z'], // 23:45 -> midnight, which is tomorrow
  ])('after %s is %s', (now, expected) => {
    expect(iso(nextSlot(utc(now), singapore))).toBe(expected)
  })

  it('is on the hour when slots are an hour, and on the wall clock in a zone that is not on the hour', () => {
    expect(iso(nextSlot(utc('2026-10-05T01:10:00Z'), { timeZone: 'Asia/Singapore', slotMinutes: 60 }))).toBe('2026-10-05T02:00:00.000Z')
    // Kathmandu is 5:45 ahead, so 10:00 on the wall there is 04:15 UTC
    expect(iso(nextSlot(utc('2026-10-05T04:00:00Z'), { timeZone: 'Asia/Kathmandu', slotMinutes: 60 }))).toBe('2026-10-05T04:15:00.000Z')
  })
})

describe('a whole day', () => {
  it('is midnight to midnight when it has not started', () => {
    const { start, end } = wholeDay(utc('2026-10-07T05:00:00Z'), utc('2026-10-05T03:00:00Z'), singapore)

    expect(iso(start)).toBe('2026-10-06T16:00:00.000Z')
    expect(iso(end)).toBe('2026-10-07T16:00:00.000Z')
  })

  it('is from the next slot when it has started', () => {
    const { start, end } = wholeDay(utc('2026-10-05T05:00:00Z'), utc('2026-10-05T03:10:00Z'), singapore)

    expect(iso(start)).toBe('2026-10-05T03:30:00.000Z')
    expect(iso(end)).toBe('2026-10-05T16:00:00.000Z')
  })
})

describe('runs of days', () => {
  const d = (month: number, day: number) => new CalendarDate(2026, month, day)

  it('are found whatever order the days are in, and across a month, and a day twice is one', () => {
    const runs = consecutiveRuns([d(11, 2), d(10, 31), d(10, 30), d(11, 1), d(11, 5), d(11, 5)])

    expect(runs.map(run => run.map(day => `${day.month}-${day.day}`))).toEqual([['10-30', '10-31', '11-1', '11-2'], ['11-5']])
  })
})

describe('planning slots', () => {
  const now = utc('2026-10-05T00:00:00Z') // 08:00 on the 5th in Singapore
  const day = (n: number) => new CalendarDate(2026, 10, n)

  it('makes a slot for each facility and each day, at the times, in the organization\'s zone', () => {
    const { slots, skipped } = planSlots({ facilityIds: ['hall', 'gym'], days: [day(7), day(9)], allDay: false, from: '09:00', to: '11:30', split: true }, now, singapore)

    expect(skipped).toBe(0)
    expect(slots.map(s => `${s.facilityId} ${iso(s.start)} ${iso(s.end)}`)).toEqual([
      'hall 2026-10-07T01:00:00.000Z 2026-10-07T03:30:00.000Z',
      'hall 2026-10-09T01:00:00.000Z 2026-10-09T03:30:00.000Z',
      'gym 2026-10-07T01:00:00.000Z 2026-10-07T03:30:00.000Z',
      'gym 2026-10-09T01:00:00.000Z 2026-10-09T03:30:00.000Z',
    ])
  })

  it('makes one booking for days in a row when they are not split, from the first start to the last end', () => {
    const { slots } = planSlots({ facilityIds: ['hall'], days: [day(7), day(8), day(9), day(12)], allDay: false, from: '09:00', to: '17:00', split: false }, now, singapore)

    expect(slots.map(s => `${iso(s.start)} ${iso(s.end)}`)).toEqual([
      '2026-10-07T01:00:00.000Z 2026-10-09T09:00:00.000Z',
      '2026-10-12T01:00:00.000Z 2026-10-12T09:00:00.000Z',
    ])
  })

  it('takes all day as midnight to midnight, and 24:00 as the midnight that ends the day', () => {
    const allDay = planSlots({ facilityIds: ['hall'], days: [day(7), day(8)], allDay: true, from: '', to: '', split: false }, now, singapore)
    const toMidnight = planSlots({ facilityIds: ['hall'], days: [day(7)], allDay: false, from: '20:00', to: '24:00', split: true }, now, singapore)

    expect(allDay.slots.map(s => `${iso(s.start)} ${iso(s.end)}`)).toEqual(['2026-10-06T16:00:00.000Z 2026-10-08T16:00:00.000Z'])
    expect(iso(toMidnight.slots[0]!.end)).toBe('2026-10-07T16:00:00.000Z')
  })

  it('leaves out and counts what would start in the past, and starts today from the next slot when it is all day', () => {
    const later = utc('2026-10-05T03:10:00Z') // 11:10 on the 5th
    const past = planSlots({ facilityIds: ['hall'], days: [day(4), day(5)], allDay: false, from: '09:00', to: '10:00', split: true }, later, singapore)
    const today = planSlots({ facilityIds: ['hall'], days: [day(5)], allDay: true, from: '', to: '', split: true }, later, singapore)

    expect(past.slots).toEqual([])
    expect(past.skipped).toBe(2)
    expect(iso(today.slots[0]!.start)).toBe('2026-10-05T03:30:00.000Z')
  })

  it('is exact on the day the clocks go forward: nine in the morning is still nine', () => {
    const { slots } = planSlots({ facilityIds: ['hall'], days: [new CalendarDate(2026, 3, 8), new CalendarDate(2026, 3, 9)], allDay: false, from: '09:00', to: '10:00', split: true }, utc('2026-03-01T00:00:00Z'), newYork)

    // 09:00 EST is 14:00 UTC, and 09:00 EDT is 13:00 UTC
    expect(slots.map(s => iso(s.start))).toEqual(['2026-03-08T13:00:00.000Z', '2026-03-09T13:00:00.000Z'])
  })
})

describe('clashes', () => {
  const now = utc('2026-10-05T00:00:00Z')
  const known: KnownBooking[] = [{ id: 'b1', facilityId: 'hall', startDateTime: utc('2026-10-07T01:00:00Z'), endDateTime: utc('2026-10-07T03:00:00Z'), conduct: 'Lesson', bookedBy: { displayName: 'SGT Lee' } }]
  const slot = (facilityId: string, from: string, to: string) => ({ facilityId, start: utc(from), end: utc(to) })

  it('are with the past, a booking, or an earlier slot, and only of the same facility', () => {
    const result = findClashes([
      slot('hall', '2026-10-04T01:00:00Z', '2026-10-04T02:00:00Z'),
      slot('hall', '2026-10-07T02:00:00Z', '2026-10-07T04:00:00Z'),
      slot('gym', '2026-10-07T02:00:00Z', '2026-10-07T04:00:00Z'),
      slot('gym', '2026-10-07T03:00:00Z', '2026-10-07T05:00:00Z'),
      // Starts as the earlier slot ends, and as the booking ends, so it doesn't clash
      slot('hall', '2026-10-07T04:00:00Z', '2026-10-07T05:00:00Z'),
    ], known, now)

    expect(result.map(c => c?.kind)).toEqual(['past', 'existing', undefined, 'selection', undefined])
    expect(result[3]).toEqual({ kind: 'selection', index: 2 })
    expect((result[1] as { booking: KnownBooking }).booking.id).toBe('b1')
  })
})

describe('describing a slot', () => {
  const df = new DateFormatter('en-SG', { dateStyle: 'medium', timeZone: 'Asia/Singapore' })
  const tf = new DateFormatter('en-SG', { timeStyle: 'short', timeZone: 'Asia/Singapore' })

  it.each([
    ['2026-10-06T16:00:00Z', '2026-10-07T16:00:00Z', '7 Oct 2026', 'All day'],
    ['2026-10-06T16:00:00Z', '2026-10-09T16:00:00Z', '7 Oct 2026 – 9 Oct 2026', 'All day, 3 days'],
    ['2026-10-07T01:00:00Z', '2026-10-07T03:30:00Z', '7 Oct 2026', '9:00 am – 11:30 am'],
    ['2026-10-07T12:00:00Z', '2026-10-07T16:00:00Z', '7 Oct 2026', '8:00 pm – midnight'],
    ['2026-10-07T01:00:00Z', '2026-10-08T09:00:00Z', '7 Oct 2026 – 8 Oct 2026', '9:00 am – 5:00 pm, 2 days'],
  ])('%s to %s', (start, end, date, time) => {
    expect(describeSlot({ start: utc(start), end: utc(end) }, df, tf, 'Asia/Singapore')).toEqual({ date, time })
  })
})

describe('moving a booking', () => {
  const now = utc('2026-10-05T03:00:00Z')
  const booking = (from: string, to: string) => ({ id: 'b1', facilityId: 'hall', start: utc(from), end: utc(to) })
  const other: KnownBooking = { id: 'b2', facilityId: 'hall', startDateTime: utc('2026-10-08T01:00:00Z'), endDateTime: utc('2026-10-08T03:00:00Z'), conduct: 'Other' }

  it('is not possible once it is over, and only its end can change once it has started', () => {
    expect(findRescheduleProblem(booking('2026-10-05T00:00:00Z', '2026-10-05T02:00:00Z'), { start: utc('2026-10-06T01:00:00Z'), end: utc('2026-10-06T02:00:00Z') }, [], now)).toEqual({ kind: 'over' })
    expect(findRescheduleProblem(booking('2026-10-05T02:00:00Z', '2026-10-05T05:00:00Z'), { start: utc('2026-10-05T04:00:00Z'), end: utc('2026-10-05T06:00:00Z') }, [], now)).toEqual({ kind: 'started' })
    expect(findRescheduleProblem(booking('2026-10-05T02:00:00Z', '2026-10-05T05:00:00Z'), { start: utc('2026-10-05T02:00:00Z'), end: utc('2026-10-05T06:00:00Z') }, [], now)).toBeUndefined()
  })

  it('is not possible into the past, backwards, or onto another booking, but is onto itself', () => {
    const future = booking('2026-10-07T01:00:00Z', '2026-10-07T03:00:00Z')

    expect(findRescheduleProblem(future, { start: utc('2026-10-04T01:00:00Z'), end: utc('2026-10-04T03:00:00Z') }, [], now)).toEqual({ kind: 'past' })
    expect(findRescheduleProblem(future, { start: utc('2026-10-07T03:00:00Z'), end: utc('2026-10-07T01:00:00Z') }, [], now)).toEqual({ kind: 'order' })
    expect(findRescheduleProblem(future, { start: utc('2026-10-08T02:00:00Z'), end: utc('2026-10-08T04:00:00Z') }, [other], now)).toMatchObject({ kind: 'existing' })
    expect(findRescheduleProblem(future, { start: utc('2026-10-07T00:00:00Z'), end: utc('2026-10-07T04:00:00Z') }, [{ ...other, id: 'b1', startDateTime: future.start, endDateTime: future.end }], now)).toBeUndefined()
  })
})
