<script setup lang="ts">
import { useGetMe } from '~/api'

// Somebody who is signed in doesn't stay on the home page: where they belong decides where they go, straight in if it is only one place.
// Only drawn in the browser (pages/index.vue), and over the page while it looks.
const { isLoaded, isSignedIn } = useAccountSession()
const router = useRouter()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const signedIn = computed(() => isLoaded.value && isSignedIn.value === true)
const { data: me, isError } = useGetMe({ query: { enabled: signedIn } })

watch(me, async (value) => {
  if (!value) {
    return
  }

  const places = value.memberships.filter(m => m.status === 'Active')
  const remembered = places.find(m => m.tenantSlug === lastOrganization.value)
  if (places.length === 1 || remembered) {
    await router.replace(`/t/${(remembered ?? places[0]!).tenantSlug}`)
  }
  else if (places.length > 1 || value.memberships.length > 0) {
    await router.replace('/orgs')
  }
  else {
    await router.replace('/onboarding')
  }
}, { immediate: true })
</script>

<template>
  <div
    v-if="signedIn && !isError"
    class="fixed inset-0 z-50 flex items-center justify-center gap-3 bg-default text-muted"
    role="status"
  >
    <UIcon
      name="i-lucide-loader-circle"
      class="size-5 animate-spin"
    />
    <span>Finding your organizations...</span>
  </div>
</template>
