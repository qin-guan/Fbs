import MiniSearch from 'minisearch'
import { useGetNominalRoll } from '~/api'

export function useNominalRollMapping() {
  return useGetNominalRoll({
    query: {
      select(data) {
        const r: Record<string, string | null | undefined> = {}
        for (const item of data ?? []) {
          r[item.phone ?? ''] = item.name
        }
        return r
      },
    },
  })
}

export const useNominalRollMiniSearch = createSharedComposable(() => {
  const { data: nominalRoll } = useGetNominalRoll()

  return computed(() => {
    if (!nominalRoll.value) return null

    const miniSearch = new MiniSearch({
      idField: 'phone',
      fields: ['name', 'phone'],
      searchOptions: {
        boost: { name: 2 },
      },
    })

    miniSearch.addAll(nominalRoll.value)

    return miniSearch
  })
})
