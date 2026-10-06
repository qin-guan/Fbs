import { client } from '~/api'

/** The organization's Google Calendar, as `GET /t/:slug/Calendar` describes it. */
export interface OrgCalendar {
  status: 'None' | 'Pending' | 'Active' | 'Failed' | 'Disabled'
  calendarId?: string | null
  lastError?: string | null
  verificationExpiresAt?: string | Date | null
  serviceAccountEmail?: string | null
}

async function send<T>(method: 'GET' | 'POST' | 'DELETE', url: string, slug: string, body?: unknown) {
  const result = await client({
    method,
    url,
    path: { slug },
    body,
    throwOnError: true,
  })
  return result.data as T
}

export function getOrgCalendar(slug: string) {
  return send<OrgCalendar>('GET', '/t/{slug}/Calendar', slug)
}

export function startOrgCalendar(slug: string, calendarId: string) {
  return send<OrgCalendar>('POST', '/t/{slug}/Calendar', slug, { calendarId })
}

export function confirmOrgCalendar(slug: string, code: string) {
  return send<OrgCalendar>('POST', '/t/{slug}/Calendar/Confirm', slug, { code })
}

export function deleteOrgCalendar(slug: string) {
  return send<undefined>('DELETE', '/t/{slug}/Calendar', slug)
}