import { describe, expect, it } from 'vitest'
import { startOfDayIn } from '../lib/slots'
import { DAY_WIDTH, HOUR_WIDTH, dayStarts, layoutTimeline, positionOf, type TimelineBooking, type TimelineFacility } from '../lib/timeline'

const utc = (iso: string) => new Date(iso)

const hall: TimelineFacility = { id: 'hall', name: 'Hall', group: 'Indoor' }
const gym: TimelineFacility = { id: 'gym', name: 'Gym', group: 'Indoor' }
const field: TimelineFacility = { id: 'field', name: 'Field', group: 'Outdoor' }

function booking(id: string, facilityId: string, start: string, end: string, facilityName?: string): TimelineBooking {
  return { id, facilityId, facilityName, startDateTime: utc(start), endDateTime: utc(end) }
}

// Monday 5 October 2026 begins at 16:00 UTC on the Sunday, in Singapore
const singapore = { timeZone: 'Asia/Singapore', from: utc('2026-10-04T16:00:00Z'), now: utc('2026-10-04T16:30:00Z') }

describe('where a booking is on the timeline', () => {
  it('is where its hour is in the organization, however far that is from UTC', () => {
    // Ten in the morning in Singapore is 02:00 UTC
    expect(positionOf(utc('2026-10-05T02:00:00Z'), singapore.from, singapore.timeZone)).toBe(10 * HOUR_WIDTH)
    expect(positionOf(utc('2026-10-05T02:30:00Z'), singapore.from, singapore.timeZone)).toBe(10.5 * HOUR_WIDTH)
  })

  it('is on the second day after the first', () => {
    expect(positionOf(utc('2026-10-06T01:00:00Z'), singapore.from, singapore.timeZone)).toBe(DAY_WIDTH + 9 * HOUR_WIDTH)
  })

  it('is where ten is when the clocks have gone back, though the day is an hour longer', () => {
    // 1 November 2026, when New York goes from 02:00 summer time to 01:00
    const from = startOfDayIn(utc('2026-11-01T12:00:00Z'), 'America/New_York')
    const tenAm = utc('2026-11-01T15:00:00Z')
    const nextDay = utc('2026-11-02T14:00:00Z')

    expect(positionOf(tenAm, from, 'America/New_York')).toBe(10 * HOUR_WIDTH)
    expect(positionOf(nextDay, from, 'America/New_York')).toBe(DAY_WIDTH + 9 * HOUR_WIDTH)
  })

  it('is where ten is when the clocks have gone forward, though the day is an hour shorter', () => {
    // 8 March 2026, when New York goes from 02:00 to 03:00
    const from = startOfDayIn(utc('2026-03-08T12:00:00Z'), 'America/New_York')

    expect(positionOf(utc('2026-03-08T14:00:00Z'), from, 'America/New_York')).toBe(10 * HOUR_WIDTH)
    expect(positionOf(utc('2026-03-09T13:00:00Z'), from, 'America/New_York')).toBe(DAY_WIDTH + 9 * HOUR_WIDTH)
  })

  it('has a day that starts at midnight there for each day of the window', () => {
    const days = dayStarts(startOfDayIn(utc('2026-11-01T12:00:00Z'), 'America/New_York'), 3, 'America/New_York')

    expect(days.map(d => d.toISOString())).toEqual(['2026-11-01T04:00:00.000Z', '2026-11-02T05:00:00.000Z', '2026-11-03T05:00:00.000Z'])
  })
})

describe('what the timeline draws', () => {
  const layout = (bookings: TimelineBooking[], more: Partial<Parameters<typeof layoutTimeline>[0]> = {}) =>
    layoutTimeline({ bookings, facilities: [hall, gym, field], days: 7, ...singapore, ...more })

  it('puts the facilities under the kind they are, and each booking in the row of its facility', () => {
    const sections = layout([booking('a', 'field', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z')])

    expect(sections.map(s => [s.group, s.rows.map(r => r.name)])).toEqual([['Indoor', ['Hall', 'Gym']], ['Outdoor', ['Field']]])
    expect(sections[1]!.rows[0]!.blocks.map(b => [b.booking.id, b.left, b.width])).toEqual([['a', 10 * HOUR_WIDTH, HOUR_WIDTH]])
  })

  it('leaves out what is over, and what is not in the window', () => {
    const sections = layout([
      booking('over', 'hall', '2026-10-04T16:00:00Z', '2026-10-04T16:20:00Z'),
      booking('before', 'hall', '2026-10-03T02:00:00Z', '2026-10-03T03:00:00Z'),
      booking('after', 'hall', '2026-10-12T02:00:00Z', '2026-10-12T03:00:00Z'),
      booking('in', 'hall', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z'),
    ])

    expect(sections[0]!.rows[0]!.blocks.map(b => b.booking.id)).toEqual(['in'])
  })

  it('draws only as much as the window shows of a booking that goes past its edges', () => {
    const sections = layout([
      booking('started', 'hall', '2026-10-04T15:00:00Z', '2026-10-04T18:00:00Z'),
      booking('runs-on', 'gym', '2026-10-11T14:00:00Z', '2026-10-11T18:00:00Z'),
    ])

    const started = sections[0]!.rows[0]!.blocks[0]!
    const runsOn = sections[0]!.rows[1]!.blocks[0]!
    expect([started.left, started.width]).toEqual([0, 2 * HOUR_WIDTH])
    expect(runsOn.left + runsOn.width).toBe(7 * DAY_WIDTH)
  })

  it('keeps the bookings of a row in the order of time', () => {
    const sections = layout([
      booking('later', 'hall', '2026-10-05T08:00:00Z', '2026-10-05T09:00:00Z'),
      booking('sooner', 'hall', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z'),
    ])

    expect(sections[0]!.rows[0]!.blocks.map(b => b.booking.id)).toEqual(['sooner', 'later'])
  })

  it('shows a facility that has gone, if it has bookings, under other, with the name it had', () => {
    const sections = layout([booking('a', 'old', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z', 'Old hall')])

    expect(sections.at(-1)!.group).toBe('Other')
    expect(sections.at(-1)!.rows.map(r => [r.name, r.blocks.length])).toEqual([['Old hall', 1]])
  })

  it('says the facility is gone when the name is not known', () => {
    const sections = layout([booking('a', 'old', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z')])

    expect(sections.at(-1)!.rows[0]!.name).toBe('A facility that is gone')
  })

  it('is only the facilities that are asked for', () => {
    const sections = layout([], { only: ['gym'] })

    expect(sections.map(s => s.rows.map(r => r.name))).toEqual([['Gym']])
  })

  it('is only the facilities with bookings, if that is asked for, and nothing if none has', () => {
    const one = layout([booking('a', 'field', '2026-10-05T02:00:00Z', '2026-10-05T03:00:00Z')], { onlyBooked: true })
    const none = layout([], { onlyBooked: true })

    expect(one.map(s => s.rows.map(r => r.name))).toEqual([['Field']])
    expect(none).toEqual([])
  })
})
