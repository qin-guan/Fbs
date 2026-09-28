import { client } from '~/api'

// The API sends dates as ISO 8601 strings with an offset
const isoDateTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/

// Turn them back into dates, as the generated types (and the rest of the app) expect
function reviveDates(value: unknown): unknown {
  if (typeof value === 'string') {
    return isoDateTime.test(value) ? new Date(value) : value
  }

  if (Array.isArray(value)) {
    return value.map(reviveDates)
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, reviveDates(item)]))
  }

  return value
}

export default defineNuxtPlugin(() => {
  client.setConfig({
    baseURL: useRuntimeConfig().public.api,
    // The session cookie belongs to the API's domain. The client only reads `credentials` per
    // request, so set it on the fetch options instead.
    options: { credentials: 'include' },
    codecs: {
      'application/json': { deserialize: reviveDates },
    },
  })
})
