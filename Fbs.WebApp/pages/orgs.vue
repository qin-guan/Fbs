<script setup lang="ts">
import { useGetMe } from '~/api'

definePageMeta({
  layout: 'account',
})

useHead({ title: 'Your organizations' })

const { data: me, isPending } = useGetMe()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const places = computed(() => me.value?.memberships ?? [])

// Where somebody can go: to what they can use, and to where they are told why they can't, for an organization that is to be deleted or paused
const leadsSomewhere = (place: { status: string }) => place.status === 'Active'

const deletedOn = (date?: Date | null) => (date ? new Intl.DateTimeFormat('en-SG', { dateStyle: 'medium' }).format(date) : undefined)

function remember(place: { tenantSlug: string, status: string, tenantStatus: string }) {
  if (place.status === 'Active' && place.tenantStatus === 'Active') {
    lastOrganization.value = place.tenantSlug
  }
}
</script>

<template>
  <div class="space-y-6">
    <UPageCard
      title="Your organizations"
      description="Choose where you're booking, or make or join another organization."
      variant="naked"
    />

    <div
      v-if="isPending"
      class="space-y-3"
    >
      <USkeleton
        v-for="n in 2"
        :key="n"
        class="h-20 w-full"
      />
    </div>

    <template v-else>
      <div
        v-if="places.length > 0"
        class="space-y-3"
      >
        <UPageCard
          v-for="place in places"
          :key="place.tenantSlug"
          :title="place.tenantName"
          :description="place.status !== 'Active' ? 'Waiting for an admin to let you in' : place.tenantStatus === 'PendingDeletion' ? `To be deleted${deletedOn(place.deleteAfter) ? `, on ${deletedOn(place.deleteAfter)} at the earliest` : ''}` : place.tenantStatus === 'Suspended' ? 'Paused' : `You're ${place.role === 'Admin' ? 'an admin' : 'a member'} here, as ${place.displayName}`"
          :to="leadsSomewhere(place) ? `/t/${place.tenantSlug}` : undefined"
          variant="subtle"
          icon="i-lucide-building-2"
          @click="remember(place)"
        >
          <template #footer>
            <UBadge
              v-if="place.status !== 'Active'"
              label="Waiting for approval"
              color="warning"
              variant="subtle"
            />
            <UBadge
              v-else-if="place.tenantStatus === 'PendingDeletion'"
              label="To be deleted"
              color="error"
              variant="subtle"
            />
            <UBadge
              v-else-if="place.tenantStatus === 'Suspended'"
              label="Paused"
              color="neutral"
              variant="subtle"
            />
            <UBadge
              v-else-if="place.role === 'Admin'"
              label="Admin"
              variant="subtle"
            />
          </template>
        </UPageCard>
      </div>

      <UAlert
        v-else
        title="You aren't in an organization yet"
        description="Make one for your unit or club, or join one with a link someone sent you."
        color="neutral"
        variant="subtle"
        icon="i-lucide-info"
      />

      <div class="flex flex-wrap gap-2">
        <UButton
          to="/onboarding"
          label="Make an organization or join one"
          icon="i-lucide-plus"
        />
      </div>
    </template>
  </div>
</template>
