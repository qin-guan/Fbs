<script setup lang="ts">
import { useGetOrgFacilities, useGetOrgInvites, useGetOrgMembers, useGetOrgUnits } from '~/api'

// What an admin does with an organization that has just been made, in the order it is done in. It goes when it has been done, or when
// they say they don't want it: they may have their own way of setting it up.
const { slug, path, isAdmin } = useTenant()
const dismissed = useLocalStorage(computed(() => `fbs:setup-hidden:${slug.value}`), false)
const wanted = computed(() => isAdmin.value && !dismissed.value)

const apiPath = computed(() => ({ slug: slug.value }))
const options = { query: { enabled: wanted } }
const { data: facilities } = useGetOrgFacilities({ path: apiPath }, options)
const { data: units } = useGetOrgUnits({ path: apiPath }, options)
const { data: invites } = useGetOrgInvites({ path: apiPath }, options)
const { data: members } = useGetOrgMembers({ path: apiPath, query: { includeRemoved: false } }, options)

const loaded = computed(() => !!facilities.value && !!units.value && !!invites.value && !!members.value)
const steps = computed(() => [
  {
    id: 'facilities',
    title: 'Add what people can book',
    description: 'Add each place people can book, such as a hall, a field, or a room. It can be open to everyone or only to some units.',
    done: (facilities.value?.length ?? 0) > 0,
    to: path('admin', 'facilities'),
    action: 'Add facilities',
  },
  {
    id: 'units',
    title: 'Add your units',
    description: 'Optional. A platoon, a team, or another group. You can limit a facility to a unit, and people in a unit can change each other\'s bookings.',
    done: (units.value?.length ?? 0) > 0,
    to: path('admin', 'units'),
    action: 'Add units',
    optional: true,
  },
  {
    id: 'people',
    title: 'Invite people',
    description: 'Send a link to the people who should book. You can also add someone by their phone number.',
    done: (invites.value?.length ?? 0) > 0 || (members.value?.length ?? 0) > 1,
    to: path('admin', 'invites'),
    action: 'Make a link',
  },
])

// Units are optional, so an organization without them is set up
const finished = computed(() => steps.value.every(step => step.done || step.optional))
const shown = computed(() => wanted.value && loaded.value && !finished.value)

function hide() {
  dismissed.value = true
}
</script>

<template>
  <section
    v-if="shown"
    aria-label="Set up your organization"
    data-testid="setup-checklist"
  >
    <UPageCard
      title="Set up your organization"
      description="A few things to finish before people can book."
      variant="subtle"
    >
      <ol class="space-y-3">
        <li
          v-for="step in steps"
          :key="step.id"
          class="flex items-start gap-3"
          :data-step="step.id"
          :data-done="step.done"
        >
          <UIcon
            :name="step.done ? 'i-lucide-circle-check' : 'i-lucide-circle'"
            class="mt-0.5 size-5 shrink-0"
            :class="step.done ? 'text-success' : 'text-dimmed'"
          />
          <div class="min-w-0 flex-1">
            <p
              class="font-medium"
              :class="step.done ? 'text-muted line-through' : 'text-highlighted'"
            >
              {{ step.title }}
            </p>
            <p class="text-sm text-muted">
              {{ step.description }}
            </p>
          </div>
          <UButton
            v-if="!step.done"
            :to="step.to"
            :label="step.action"
            size="sm"
            color="neutral"
            variant="subtle"
          />
        </li>
      </ol>

      <template #footer>
        <UButton
          label="Hide this"
          size="xs"
          color="neutral"
          variant="ghost"
          @click="hide"
        />
      </template>
    </UPageCard>
  </section>
</template>
