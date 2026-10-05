import { CalendarDateTime, DateFormatter, fromDate, now, toCalendarDate, toZoned } from '@internationalized/date'
import { useQueryClient, type QueryClient } from '@tanstack/vue-query'
import { useGetOrg } from '~/api'

/** The organization in the address, and who the caller is in it. Every page under /t/:slug asks, and shares the one answer. */
export function useTenant() {
  const route = useRoute()
  const slug = computed(() => String(route.params.slug))
  const query = useGetOrg({ path: computed(() => ({ slug: slug.value })) }, { query: { retry: false, staleTime: 60_000 } })
  const org = query.data
  const isAdmin = computed(() => org.value?.me.role === 'Admin')
  const timeZone = computed(() => org.value?.timeZone ?? 'UTC')

  /** An address inside the organization, such as `path('bookings', id)`. */
  function path(...parts: string[]) {
    return ['/t', slug.value, ...parts].join('/')
  }

  return { ...query, slug, org, isAdmin, timeZone, path }
}

/** Dates and times as they are in the organization's time zone, whichever the browser is in. */
export function useTenantFormatter() {
  const { timeZone } = useTenant()
  return {
    timeZone,
    df: computed(() => new DateFormatter('en-SG', { dateStyle: 'medium', timeZone: timeZone.value })),
    tf: computed(() => new DateFormatter('en-SG', { timeStyle: 'short', timeZone: timeZone.value })),
    dtf: computed(() => new DateFormatter('en-SG', { dateStyle: 'medium', timeStyle: 'short', timeZone: timeZone.value })),
  }
}

/** Midnight at the start of today in the time zone, or of a day some way from today. */
export function startOfToday(timeZone: string, addDays = 0): Date {
  const today = toCalendarDate(now(timeZone)).add({ days: addDays })
  return toZoned(today, timeZone).toDate()
}

/** A time as `2026-10-05T09:00`, in the time zone: what a date and time input shows and gives back. */
export function toLocalInput(date: Date, timeZone: string) {
  const z = fromDate(date, timeZone)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${z.year}-${pad(z.month)}-${pad(z.day)}T${pad(z.hour)}:${pad(z.minute)}`
}

/** The time that a date and time input, such as `2026-10-05T09:00`, means in the time zone. */
export function fromLocalInput(value: string, timeZone: string): Date | undefined {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value)
  if (!match) {
    return undefined
  }

  const [year, month, day, hour, minute] = match.slice(1).map(Number) as [number, number, number, number, number]
  return toZoned(new CalendarDateTime(year, month, day, hour, minute), timeZone).toDate()
}

/** Forgets what was fetched of one part of an organization, such as `Units`, so lists and details are read again. */
export function invalidateUnder(queryClient: QueryClient, slug: string, ...parts: string[]) {
  return queryClient.invalidateQueries({
    predicate: (query) => {
      const key = query.queryKey[0] as { url?: string, params?: { slug?: string } } | undefined
      return parts.some(part => key?.url === `/t/:slug/${part}` || key?.url?.startsWith(`/t/:slug/${part}/`)) && key?.params?.slug === slug
    },
  })
}

/** Forgets what was fetched of an organization's bookings, so lists and details are read again. */
export function invalidateBookings(queryClient: QueryClient, slug: string) {
  return invalidateUnder(queryClient, slug, 'Bookings')
}

export function useInvalidateBookings() {
  const queryClient = useQueryClient()
  return (slug: string) => invalidateBookings(queryClient, slug)
}
