export const slugPattern = /^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$/

/** What an organization is called in its address, from what it is called: lower case letters, digits and hyphens. */
export function slugify(name: string) {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 63)
    .replace(/-+$/g, '')
}

export function isValidSlug(slug: string) {
  return slugPattern.test(slug) && !slug.includes('--')
}

/** The token in what somebody pastes for a link to join with: the whole link, or only its end. */
export function tokenFromInvite(pasted: string) {
  const text = pasted.trim()
  const match = /\/join\/([A-Za-z0-9_-]+)/.exec(text)
  const token = match ? match[1]! : text
  return /^[A-Za-z0-9_-]{20,}$/.test(token) ? token : undefined
}

const callingCodes: Record<string, string> = {
  'Asia/Singapore': '65',
  'Asia/Kuala_Lumpur': '60',
  'Asia/Jakarta': '62',
  'Asia/Kolkata': '91',
  'Asia/Hong_Kong': '852',
  'Asia/Tokyo': '81',
  'Asia/Seoul': '82',
  'Asia/Shanghai': '86',
  'Europe/London': '44',
  'Europe/Berlin': '49',
  'Europe/Paris': '33',
}

/** The calling code that phone numbers without one are most likely in, for somebody in this time zone. */
export function callingCodeFor(timeZone: string) {
  if (callingCodes[timeZone]) {
    return callingCodes[timeZone]!
  }

  if (timeZone.startsWith('Australia/')) {
    return '61'
  }

  return timeZone.startsWith('America/') ? '1' : '65'
}

/** Every time zone the browser knows, and UTC, which some leave out. */
export function knownTimeZones() {
  const zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
  return zones.includes('UTC') ? zones : ['UTC', ...zones]
}
