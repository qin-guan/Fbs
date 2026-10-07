<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query'
import { getMeQueryKey, useGetInvitesByToken, useGetMe, usePostInvitesByTokenAccept } from '~/api'

definePageMeta({
  layout: 'account',
})

useHead({ title: 'Join' })

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const toast = useToast()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const token = computed(() => String(route.params.token))
const { data: invite, isPending, error } = useGetInvitesByToken({ path: computed(() => ({ token: token.value })) }, { query: { retry: false } })
const { data: me } = useGetMe()

const displayName = ref('')
watch(me, (value) => {
  if (value && !displayName.value) {
    displayName.value = value.name ?? ''
  }
}, { immediate: true })

const outcome = ref<{ slug: string, status: string, name: string } | undefined>()
const removed = ref(false)
const full = ref(false)
const { mutateAsync: accept, isPending: joining } = usePostInvitesByTokenAccept()

async function join() {
  try {
    const joined = await accept({ path: { token: token.value }, body: { displayName: displayName.value.trim() || undefined } })
    await queryClient.invalidateQueries({ queryKey: getMeQueryKey() })
    if (joined.status === 'Active') {
      lastOrganization.value = joined.slug
      await router.push(`/t/${joined.slug}`)
    }
    else {
      outcome.value = { slug: joined.slug, status: joined.status, name: joined.organizationName }
    }
  }
  catch (e) {
    if (getErrorCodes(e).includes('removed')) {
      removed.value = true
    }
    else if (getErrorCodes(e).includes('member-limit')) {
      full.value = true
    }
    else if (getErrorStatus(e) === 404) {
      await queryClient.invalidateQueries({ queryKey: [{ url: '/Invites/:token' }] })
    }
    else {
      toast.add({ title: 'Couldn\'t join', description: 'Something went wrong. Try again.', color: 'error' })
    }
  }
}
</script>

<template>
  <div class="space-y-6">
    <UAlert
      v-if="full"
      title="This organization is full"
      description="It's at the people limit. An admin has to free a place before this link will work."
      color="warning"
      variant="subtle"
      icon="i-lucide-users-round"
    />

    <UPageCard
      v-if="outcome"
      :title="`You asked to join ${outcome.name}`"
      description="An admin has to let you in before you can use it. Leaving and coming back to this page is fine."
      variant="subtle"
      icon="i-lucide-hourglass"
    >
      <UButton
        to="/orgs"
        label="Your organizations"
        color="neutral"
        variant="subtle"
      />
    </UPageCard>

    <UAlert
      v-else-if="removed"
      title="An admin removed you from this organization"
      description="Only an admin can let you back in."
      color="warning"
      variant="subtle"
      icon="i-lucide-triangle-alert"
    />

    <div
      v-else-if="isPending"
      class="space-y-3"
    >
      <USkeleton class="h-24 w-full" />
    </div>

    <UPageCard
      v-else-if="error || !invite"
      title="This link doesn't work"
      description="It expired, ran out of uses, or somebody stopped it. Ask whoever sent it for another."
      variant="subtle"
      icon="i-lucide-link-2-off"
    >
      <UButton
        to="/onboarding"
        label="Make an organization instead"
        color="neutral"
        variant="subtle"
      />
    </UPageCard>

    <UPageCard
      v-else
      :title="`Join ${invite.organizationName}`"
      :description="invite.requiresApproval ? 'After you ask, an admin lets you in.' : 'This adds you to the organization.'"
      variant="subtle"
      icon="i-lucide-user-plus"
    >
      <form
        class="space-y-4"
        @submit.prevent="join"
      >
        <UFormField
          label="Your name"
          description="What other people see. Rank and name, for instance."
        >
          <UInput
            v-model="displayName"
            class="w-full"
            autocomplete="name"
          />
        </UFormField>

        <UButton
          type="submit"
          :label="invite.requiresApproval ? 'Ask to join' : 'Join'"
          icon="i-lucide-user-plus"
          :loading="joining"
        />
      </form>
    </UPageCard>
  </div>
</template>
