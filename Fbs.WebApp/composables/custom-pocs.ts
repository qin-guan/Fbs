export type CustomPoc = {
  name: string
  phone: string
}

const MAX_CUSTOM_POCS = 20

export function normalizePocPhone(phone?: string | null) {
  const digits = phone?.replace(/\D/g, '') ?? ''
  if (/^65\d{8}$/.test(digits)) return digits
  if (/^\d{8}$/.test(digits)) return `65${digits}`
  return undefined
}

export function useCustomPocs() {
  const stored = useLocalStorage<CustomPoc[]>('custom-pocs', [])
  const { data: nominalRoll } = useNominalRollMapping()

  const customPocs = computed(() => {
    const roll = nominalRoll.value
    return stored.value.filter((poc) => {
      if (!poc.name || !poc.phone) return false
      // Drop entries that now exist on the nominal roll under the same name.
      return roll?.[poc.phone] !== poc.name
    })
  })

  function remember(poc: { name?: string | null, phone?: string | null }) {
    const name = poc.name?.trim()
    const phone = normalizePocPhone(poc.phone)
    if (!name || !phone) return
    if (nominalRoll.value?.[phone] === name) return

    const next = stored.value.filter(
      existing => existing.phone !== phone && existing.name.toLowerCase() !== name.toLowerCase(),
    )
    next.unshift({ name, phone })
    stored.value = next.slice(0, MAX_CUSTOM_POCS)
  }

  return { customPocs, remember }
}
