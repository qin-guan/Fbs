<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query'
import { usePutOrgMe, type FbsWebApiEndpointsMeGetMembership, type FbsWebApiEndpointsMeGetResponse } from '~/api'

type Place = FbsWebApiEndpointsMeGetMembership

const props = defineProps<{ me?: FbsWebApiEndpointsMeGetResponse }>()

const toast = useToast()
const queryClient = useQueryClient()

const places = computed(() => props.me?.memberships ?? [])
// What an organization is given from the account, as the API has it: the name, or the email if there is none
const accountName = computed(() => props.me?.name ?? props.me?.email ?? undefined)
const followsAccount = (place: Place) => place.displayName === accountName.value
// Only somebody who is in, in an organization that is open, can change anything in it
const canChange = (place: Place) => place.status === 'Active' && place.tenantStatus === 'Active'

const changing = ref<string>()
const changeTo = ref('')
const problem = ref<string>()
function startChange(place: Place) {
  changing.value = place.tenantSlug
  changeTo.value = place.displayName
  problem.value = undefined
}
function takeAccountName() {
  changeTo.value = accountName.value ?? changeTo.value
  problem.value = undefined
}

const { mutateAsync: rename, isPending: saving } = usePutOrgMe()
async function save(place: Place) {
  const displayName = changeTo.value.trim()
  if (displayName.length < 1 || displayName.length > 200) {
    problem.value = 'The name has to be between 1 and 200 characters.'
    return
  }

  try {
    await rename({ path: { slug: place.tenantSlug }, body: { displayName } })
    changing.value = undefined
    // It is on their bookings, the list of people and the history too
    await queryClient.invalidateQueries()
    toast.add({ title: `You're ${displayName} in ${place.tenantName}`, color: 'success' })
  }
  catch (e) {
    problem.value = getFieldErrors(e)[0]?.message ?? getErrorReasons(e)[0] ?? 'Couldn\'t change it. Try again.'
  }
}
</script>

<template>
  <UPageCard
    title="Your name in each organization"
    description="What people in an organization see on your bookings. Each one has its own: a name that's the same as the one on your account changes when that does, and one that's different stays as it is."
    variant="subtle"
    icon="i-lucide-id-card"
  >
    <p
      v-if="!places.length"
      class="text-sm text-muted"
    >
      You're not in an organization yet.
    </p>

    <ul
      v-else
      class="divide-y divide-default rounded-lg border border-default"
    >
      <li
        v-for="place in places"
        :key="place.tenantSlug"
        class="p-3"
      >
        <form
          v-if="changing === place.tenantSlug"
          class="space-y-2"
          @submit.prevent="save(place)"
        >
          <p class="text-xs text-muted">
            {{ place.tenantName }}
          </p>
          <div class="flex items-start gap-2">
            <UFormField
              :error="problem"
              class="flex-1"
            >
              <UInput
                v-model="changeTo"
                class="w-full"
                :aria-label="`Your name in ${place.tenantName}`"
                autocomplete="name"
                autofocus
                @update:model-value="problem = undefined"
              />
            </UFormField>
            <UButton
              type="submit"
              label="Save"
              :loading="saving"
            />
            <UButton
              type="button"
              label="Cancel"
              color="neutral"
              variant="ghost"
              @click="changing = undefined"
            />
          </div>
          <UButton
            v-if="accountName && changeTo.trim() !== accountName"
            type="button"
            :label="`Use the name on your account, ${accountName}`"
            color="neutral"
            variant="link"
            class="px-0"
            @click="takeAccountName"
          />
          <p
            v-else-if="accountName"
            class="text-xs text-muted"
          >
            It's the name on your account, so it changes when that does.
          </p>
        </form>

        <div
          v-else
          class="flex items-center gap-3"
        >
          <div class="min-w-0 flex-1">
            <p class="truncate font-medium text-highlighted">
              {{ place.displayName }}
            </p>
            <p class="truncate text-xs text-muted">
              {{ place.tenantName }} · {{ followsAccount(place) ? 'the name on your account' : 'only in this organization' }}
            </p>
          </div>
          <UButton
            v-if="canChange(place)"
            type="button"
            icon="i-lucide-pencil"
            color="neutral"
            variant="ghost"
            :aria-label="`Change your name in ${place.tenantName}`"
            @click="startChange(place)"
          />
        </div>
      </li>
    </ul>
  </UPageCard>
</template>
