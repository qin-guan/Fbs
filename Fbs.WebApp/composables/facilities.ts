import { useQuery } from '@tanstack/vue-query'

export function useFacilities() {
  return useQuery({
    queryKey: ['facilities'],
    queryFn: () => $api.facility.get(),
  })
}
