import MiniSearch from 'minisearch'
import { useQuery } from '@tanstack/vue-query'

export function useNominalRoll() {
  return useQuery({
    queryKey: ['nominal-roll'],
    queryFn: () => $api.nominalRoll.get(),
  })
}

export function useNominalRollMapping() {
  return useQuery({
    queryKey: ['nominal-roll-names'],
    queryFn: () => $api.nominalRoll.get(),
    select(data) {
      const r: Record<string, string | null | undefined> = {}
      for (const item of data ?? []) {
        r[item.phone ?? ''] = item.name
      }
      return r
    },
  })
}

export const useNominalRollMiniSearch = createSharedComposable(() => {
  const { data: nominalRoll } = useNominalRoll()

  return computed(() => {
    if (!nominalRoll.value) return null

    const miniSearch = new MiniSearch({
      idField: 'phone',
      fields: ['name', 'phone'],
      searchOptions: {
        boost: { name: 2 }
      },
    })

    miniSearch.addAll(nominalRoll.value)

    return miniSearch
  })
})
