<script setup lang="ts">
import { useGetClaimsBySlug, useGetMe, usePostClaimsBySlugStart } from '~/api'

definePageMeta({
  layout: 'account',
})

useHead({ title: 'Take over your place' })

const route = useRoute()
const router = useRouter()
const toast = useToast()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const slug = computed(() => String(route.params.slug))
const { data: claim, isPending, error } = useGetClaimsBySlug({ path: computed(() => ({ slug: slug.value })) }, { query: { retry: false } })

const link = ref<{ url: string, expiresAt: Date } | undefined>()
const { mutateAsync: start, isPending: starting } = usePostClaimsBySlugStart()

async function makeLink() {
  try {
    const started = await start({ path: { slug: slug.value } })
    link.value = { url: started.url, expiresAt: started.expiresAt }
  }
  catch {
    toast.add({ title: 'Couldn\'t make the link', description: 'Try again in a moment.', color: 'error' })
  }
}

// Once the link has been opened in Telegram, the place is theirs, and the app can go in
const waiting = computed(() => link.value !== undefined)
const { data: me } = useGetMe({ query: { refetchInterval: computed(() => waiting.value ? 3000 : false) } })
watch(me, async (value) => {
  if (waiting.value && value?.memberships.some(m => m.tenantSlug === slug.value && m.status === 'Active')) {
    lastOrganization.value = slug.value
    await router.push(`/t/${slug.value}`)
  }
})
</script>

<template>
  <div class="space-y-6">
    <div
      v-if="isPending"
      class="space-y-3"
    >
      <USkeleton class="h-24 w-full" />
    </div>

    <UPageCard
      v-else-if="error || !claim"
      title="There is nothing to take over here"
      description="Either this organization isn't handing out places any more, or you are in it already."
      variant="subtle"
      icon="i-lucide-circle-slash"
    >
      <UButton
        to="/"
        label="Go to your organizations"
        color="neutral"
        variant="subtle"
      />
    </UPageCard>

    <UPageCard
      v-else
      :title="`Take over your place in ${claim.organizationName}`"
      description="You were in the old version with your phone number. To show it is you, open a link in the same Telegram chat as before, where the bot sent your login codes."
      variant="subtle"
      icon="i-lucide-user-check"
    >
      <template v-if="!link">
        <UButton
          label="Get my link"
          icon="i-lucide-link"
          :loading="starting"
          @click="makeLink"
        />
      </template>

      <template v-else>
        <div class="space-y-3">
          <UButton
            :to="link.url"
            target="_blank"
            label="Open in Telegram"
            icon="i-simple-icons-telegram"
          />
          <p class="text-sm text-muted">
            The link works once, for ten minutes. This page goes on by itself when the bot has given you your place.
          </p>
          <p class="text-sm text-muted">
            If the bot says the chat isn't linked to anyone, you opened it from a different Telegram account than before.
          </p>
        </div>
      </template>
    </UPageCard>
  </div>
</template>
