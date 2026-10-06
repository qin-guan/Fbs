<script setup lang="ts">
import { useGetMe } from '~/api'

const { isLoaded, isSignedIn } = useAccountSession()
const router = useRouter()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const signedIn = computed(() => isLoaded.value && isSignedIn.value === true)
const { data: me, isError } = useGetMe({ query: { enabled: signedIn } })

// Where somebody who is signed in belongs decides where they go: straight in if it is only one place
const active = computed(() => me.value?.memberships.filter(m => m.status === 'Active') ?? [])
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

const links = [
  { label: 'Sign in', to: '/sign-in', trailingIcon: 'i-lucide-arrow-right' },
  { label: 'Create an account', to: '/sign-up', color: 'neutral' as const, variant: 'subtle' as const },
]

const features = [
  {
    title: 'See what\'s booked',
    description: 'Each facility\'s schedule on a day or week timeline, grouped by the kind of facility.',
    icon: 'i-lucide-calendar-range',
  },
  {
    title: 'Book it together',
    description: 'Book several slots at once. If one can\'t be booked, none of them are, and the people who need to know are told.',
    icon: 'i-lucide-calendar-check',
  },
  {
    title: 'Your organization',
    description: 'Your units, your facilities and your people. Invite them with a link.',
    icon: 'i-lucide-users-round',
  },
]
</script>

<template>
  <div class="w-full">
    <div
      v-if="signedIn && !isError"
      class="flex flex-1 items-center justify-center gap-3 py-24 text-muted"
    >
      <UIcon
        name="i-lucide-loader-circle"
        class="size-5 animate-spin"
      />
      <span>Finding your organizations{{ active.length > 1 ? '' : '...' }}</span>
    </div>

    <template v-else>
      <UPageHero
        title="Book A Space"
        description="Check what is free and book it for your conduct, from your phone. For units, clubs and anyone else who shares facilities."
        :links="signedIn ? [{ label: 'Try again', to: '/', trailingIcon: 'i-lucide-arrow-right' }] : links"
        :ui="{ container: 'py-16 sm:py-24 lg:py-28' }"
      >
        <template #top>
          <div class="absolute inset-0 -z-10 bg-[radial-gradient(60%_50%_at_50%_0%,var(--ui-color-primary-100),transparent)] dark:bg-[radial-gradient(60%_50%_at_50%_0%,color-mix(in_oklab,var(--ui-color-primary-900)_45%,transparent),transparent)]" />
        </template>
      </UPageHero>

      <UContainer class="pb-16 sm:pb-24">
        <UPageGrid>
          <UPageCard
            v-for="feature in features"
            :key="feature.title"
            v-bind="feature"
            variant="subtle"
          />
        </UPageGrid>
      </UContainer>
    </template>
  </div>
</template>
