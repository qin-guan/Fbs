<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query'
import { getMeQueryKey, getOrgQueryKey, useDeleteTenantsDeletion, useGetMe } from '~/api'

const props = defineProps<{
  error: unknown
}>()

const queryClient = useQueryClient()
const toast = useToast()
// From the address, not from useTenant(): that asks for the organization again each time it is mounted, and this is shown because it can't be had
const slug = computed(() => String(useRoute().params.slug))

// What went wrong decides what is said: an organization that isn't there, or that they aren't in, look the same on purpose
const status = computed(() => getErrorStatus(props.error))
const code = computed(() => (getProblemDetails(props.error) as { code?: string } | undefined)?.code)

// One that is to be deleted says when, and an admin of it can take that back, until it has been
const { data: me } = useGetMe()
const place = computed(() => me.value?.memberships.find(m => m.tenantSlug === slug.value))
const deletedOn = computed(() => (place.value?.deleteAfter ? new Intl.DateTimeFormat('en-SG', { dateStyle: 'medium' }).format(place.value.deleteAfter) : undefined))
const canRestore = computed(() => code.value === 'pending-deletion' && place.value?.role === 'Admin')

const { mutateAsync: restore, isPending: restoring } = useDeleteTenantsDeletion()
async function restoreIt() {
  try {
    await restore({ path: { slug: slug.value }, body: undefined })
    await queryClient.invalidateQueries({ queryKey: getMeQueryKey() })
    await queryClient.invalidateQueries({ queryKey: getOrgQueryKey({ path: { slug: slug.value } }) })
    toast.add({ title: 'The organization is back', color: 'success' })
  }
  catch (e) {
    toast.add({ title: 'Couldn\'t restore it', description: getErrorReasons(e)[0] ?? 'Try again in a moment.', color: 'error' })
  }
}

const shown = computed(() => {
  switch (code.value) {
    case 'pending':
      return { icon: 'i-lucide-hourglass', title: 'Waiting for an admin', description: 'An admin has to let you in before you can use it.' }
    case 'pending-deletion':
      return {
        icon: 'i-lucide-trash-2',
        title: 'This organization is to be deleted',
        description: `An admin asked for it to be deleted${deletedOn.value ? `, on ${deletedOn.value} at the earliest` : ''}. Nobody can use it until then${canRestore.value ? ', but you can restore it' : ', and any admin of it can restore it'}.`,
      }
    case 'unavailable':
      return { icon: 'i-lucide-circle-slash', title: 'This organization isn\'t available', description: 'It has been paused. Ask its admins.' }
    default:
      return status.value === 404
        ? { icon: 'i-lucide-circle-slash', title: 'There is no such organization', description: 'It may have a different address, or you may not be in it.' }
        : { icon: 'i-lucide-circle-slash', title: 'Something went wrong', description: 'Try again in a moment.' }
  }
})
</script>

<template>
  <div class="flex flex-1 items-center justify-center p-6">
    <UPageCard
      class="max-w-md"
      variant="subtle"
      :icon="shown.icon"
      :title="shown.title"
      :description="shown.description"
    >
      <div class="flex flex-wrap gap-2">
        <UButton
          v-if="canRestore"
          label="Restore it"
          icon="i-lucide-undo-2"
          :loading="restoring"
          @click="restoreIt"
        />
        <UButton
          to="/orgs"
          label="Your organizations"
          color="neutral"
          variant="subtle"
        />
      </div>
    </UPageCard>
  </div>
</template>
