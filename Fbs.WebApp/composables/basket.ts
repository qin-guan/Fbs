import type { BasketSlot, Slot } from '~/lib/slots'

interface StoredSlot {
  id: string
  facilityId: string
  start: string
  end: string
}

function newId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/**
 * Slots picked for booking, kept across pages (and reloads) until they're booked. Each organization has its own, so what is being
 * booked in one is never booked in another.
 */
export function useTenantBasket() {
  const { slug } = useTenant()
  const stored = useSessionStorage<StoredSlot[]>(`booking-basket:${slug.value}`, [])

  const slots = computed<BasketSlot[]>(() => stored.value.map(s => ({ id: s.id, facilityId: s.facilityId, start: new Date(s.start), end: new Date(s.end) })))

  function add(newSlots: Slot[]) {
    stored.value = [...stored.value, ...newSlots.map(s => ({ id: newId(), facilityId: s.facilityId, start: s.start.toISOString(), end: s.end.toISOString() }))]
  }

  function remove(ids: string[]) {
    stored.value = stored.value.filter(s => !ids.includes(s.id))
  }

  function clear() {
    stored.value = []
  }

  return { slots, add, remove, clear }
}
