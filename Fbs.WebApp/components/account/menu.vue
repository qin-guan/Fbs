<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import { UserButton } from '@clerk/vue'

const mode = useAuthMode()
const { profile, signOut } = useAccountSession()

// With WorkOS there is no menu of its own, as Clerk has, so it is ours: who is signed in, their account here, and signing out
const items = computed<DropdownMenuItem[][]>(() => [
  [{ type: 'label', label: profile.value?.name ?? profile.value?.email ?? 'Signed in', description: profile.value?.name ? profile.value.email : undefined }],
  [
    { label: 'Your account', icon: 'i-lucide-user-round', to: '/account' },
    { label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => signOut() },
  ],
])
</script>

<template>
  <UserButton
    v-if="mode === 'clerk'"
    after-sign-out-url="/"
  />
  <UDropdownMenu
    v-else-if="mode === 'workos'"
    :items="items"
    :content="{ align: 'end' }"
  >
    <UButton
      color="neutral"
      variant="ghost"
      square
      aria-label="Your account"
    >
      <UAvatar
        :src="profile?.pictureUrl ?? undefined"
        :alt="profile?.name ?? profile?.email ?? undefined"
        icon="i-lucide-user-round"
        size="sm"
      />
    </UButton>
  </UDropdownMenu>
  <UAvatar
    v-else
    icon="i-lucide-user-round"
    size="sm"
  />
</template>
