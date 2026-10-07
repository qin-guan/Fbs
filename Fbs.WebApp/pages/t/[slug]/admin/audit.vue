<script setup lang="ts">
import { useGetOrgAudit, type FbsWebApiEndpointsOrgAuditGetResponse } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'History' })

type Entry = FbsWebApiEndpointsOrgAuditGetResponse

const PAGE = 50
const { slug, isAdmin } = useTenant()
const { dtf } = useTenantFormatter()

// A page at a time, the latest first, and each older one is asked for from the time of the last one seen. The pages are kept by
// where they start, so that reading the first again for what is new does not put those after the older ones.
const cursor = ref<Date>()
const pages = reactive(new Map<string, Entry[]>())
const { data, isPending, isFetching, error } = useGetOrgAudit(
  { path: computed(() => ({ slug: slug.value })), query: computed(() => ({ before: cursor.value, limit: PAGE })) },
  { query: { enabled: isAdmin } },
)
watch(data, (page) => {
  if (page) {
    pages.set(cursor.value?.toISOString() ?? 'first', page)
  }
}, { immediate: true })

const entries = computed(() => {
  const seen = new Map<string, Entry>()
  for (const page of pages.values()) {
    for (const entry of page) {
      seen.set(entry.id!, entry)
    }
  }

  return [...seen.values()].sort((a, b) => b.at!.getTime() - a.at!.getTime())
})

// If the last page asked for was full, there may be older ones
const hasOlder = computed(() => (data.value?.length ?? 0) === PAGE)
function showOlder() {
  cursor.value = entries.value.at(-1)?.at
}

const icons: Record<string, string> = {
  settings: 'i-lucide-settings',
  unit: 'i-lucide-layers',
  facility: 'i-lucide-building-2',
  invite: 'i-lucide-link',
  member: 'i-lucide-users-round',
  tenant: 'i-lucide-shield',
}
const iconOf = (entry: Entry) => icons[entry.action.split('.')[0] ?? ''] ?? 'i-lucide-history'
</script>

<template>
  <TenantAdminGate>
    <UDashboardPanel id="audit">
      <template #header>
        <AppNavbar title="History" />
      </template>

      <template #body>
        <div class="max-w-3xl space-y-4">
          <p class="text-sm text-muted">
            Settings, units, facilities, invite links and people, newest at the top. Bookings are on the bookings page.
          </p>

          <UAlert
            v-if="error"
            title="Couldn't load the history"
            :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          />

          <div
            v-if="isPending"
            class="flex justify-center py-6 text-muted"
          >
            <UIcon
              name="i-lucide-loader-circle"
              class="size-6 animate-spin"
            />
          </div>

          <p
            v-else-if="!entries.length && !error"
            class="py-6 text-center text-muted"
          >
            Nothing has been done yet.
          </p>

          <ol
            v-if="entries.length"
            class="divide-y divide-default rounded-lg border border-default"
          >
            <li
              v-for="entry in entries"
              :key="entry.id"
              class="flex items-start gap-3 p-3"
              :data-action="entry.action"
            >
              <UIcon
                :name="iconOf(entry)"
                class="mt-0.5 size-5 shrink-0 text-muted"
              />
              <div class="min-w-0 flex-1">
                <p class="font-medium text-highlighted">
                  {{ entry.summary }}
                </p>
                <p class="text-sm text-muted">
                  <span data-testid="actor">{{ entry.actor?.displayName ?? 'Whoever runs the system' }}</span>
                  <template v-if="entry.target && entry.target.memberId !== entry.actor?.memberId">
                    · to <span data-testid="target">{{ entry.target.displayName }}</span>
                  </template>
                  · {{ dtf.format(entry.at!) }}
                </p>
              </div>
            </li>
          </ol>

          <div
            v-if="hasOlder"
            class="flex justify-center"
          >
            <UButton
              label="Show older"
              color="neutral"
              variant="subtle"
              :loading="isFetching"
              @click="showOlder"
            />
          </div>
        </div>
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
