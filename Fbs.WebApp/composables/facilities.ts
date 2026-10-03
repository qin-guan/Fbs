import type { FbsWebApiEndpointsOrgFacilitiesBookableGetResponse } from '~/api'

/** Facilities grouped by what kind they are, for a menu: with the kind as a label above each. */
export function facilityMenuItems(facilities: FbsWebApiEndpointsOrgFacilitiesBookableGetResponse[] | undefined) {
  const groups = new Map<string, FbsWebApiEndpointsOrgFacilitiesBookableGetResponse[]>()
  for (const facility of facilities ?? []) {
    const group = facility.group ?? 'Other'
    groups.set(group, [...(groups.get(group) ?? []), facility])
  }

  return [...groups].map(([group, members]) => [
    { type: 'label' as const, label: group },
    ...members.map(f => ({ label: f.name, value: f.id })),
  ])
}
