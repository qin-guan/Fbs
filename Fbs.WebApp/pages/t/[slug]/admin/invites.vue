<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { useDeleteOrgInvitesById, useGetOrgInvites, useGetOrgSettings, useGetOrgUnits, usePostOrgInvites, type FbsWebApiEndpointsOrgInvitesInviteResponse } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Invite links' })

type Invite = FbsWebApiEndpointsOrgInvitesInviteResponse
type Role = Invite['role']

const queryClient = useQueryClient()
const toast = useToast()
const origin = useRequestURL().origin
const { slug, isAdmin } = useTenant()
const { df } = useTenantFormatter()
const path = computed(() => ({ slug: slug.value }))

const { data: invites, isPending, error } = useGetOrgInvites({ path }, { query: { enabled: isAdmin } })
const { data: units } = useGetOrgUnits({ path }, { query: { enabled: isAdmin } })
const { data: settings } = useGetOrgSettings({ path }, { query: { enabled: isAdmin } })

const unitName = (id?: string | null) => (id ? units.value?.find(u => u.id === id)?.name : undefined)
const NO_UNIT = 'none'
const unitItems = computed(() => [{ label: 'No unit', value: NO_UNIT }, ...[...(units.value ?? [])].sort((a, b) => a.name.localeCompare(b.name)).map(u => ({ label: u.name, value: u.id }))])
const roleItems: Array<{ label: string, value: Role }> = [{ label: 'Member', value: 'Member' }, { label: 'Admin', value: 'Admin' }]

const refresh = () => invalidateUnder(queryClient, slug.value, 'Invites')

const statusLabel = { Active: 'Works', Expired: 'Ran out of time', Revoked: 'Stopped', UsedUp: 'Used up' } as const
const statusColor = { Active: 'success', Expired: 'neutral', Revoked: 'neutral', UsedUp: 'neutral' } as const

// Making one
const form = useTemplateRef('form')
const state = reactive({ role: 'Member' as Role, unit: NO_UNIT, expiresInDays: 7, maxUses: 10 })
const problem = ref<string>()
const made = ref<{ link: string, role: Role, unit?: string, expiresAt: Date, maxUses: number }>()
const { mutateAsync: create, isPending: creating } = usePostOrgInvites()
const { copy, copied, isSupported } = useClipboard({ copiedDuring: 2000 })

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (!Number.isInteger(values.expiresInDays) || values.expiresInDays < 1 || values.expiresInDays > 30) {
    errors.push({ name: 'expiresInDays', message: 'It can work for 1 to 30 days.' })
  }

  if (!Number.isInteger(values.maxUses) || values.maxUses < 1 || values.maxUses > 100) {
    errors.push({ name: 'maxUses', message: 'Between 1 and 100 people can join with it.' })
  }

  return errors
}

async function submit() {
  problem.value = undefined
  try {
    const invite = await create({
      path: path.value,
      body: { role: state.role, unitId: state.unit === NO_UNIT ? null : state.unit, expiresInDays: state.expiresInDays, maxUses: state.maxUses },
    })
    made.value = { link: `${origin}/join/${invite.token}`, role: invite.role, unit: unitName(invite.unitId), expiresAt: invite.expiresAt, maxUses: invite.maxUses }
    await refresh()
  }
  catch (e) {
    const fields = getFieldErrors(e).filter(f => ['role', 'unitId', 'expiresInDays', 'maxUses'].includes(String(f.name)))
    if (fields.length) {
      form.value?.setErrors(fields)
    }
    else {
      // Such as there being as many links as there can be
      problem.value = getErrorReasons(e)[0] ?? 'Couldn\'t make the link. Try again.'
    }
  }
}

async function copyLink() {
  if (made.value) {
    await copy(made.value.link)
  }
}

// Stopping one from working, which is not undone
const { mutateAsync: revoke, isPending: revoking } = useDeleteOrgInvitesById()
const stopping = ref<Invite>()
const stoppingOpen = computed({
  get: () => !!stopping.value,
  set: (open: boolean) => {
    if (!open) {
      stopping.value = undefined
    }
  },
})
function askStop(invite: Invite) {
  stopping.value = invite
}

async function confirmStop() {
  if (!stopping.value) {
    return
  }

  try {
    await revoke({ path: { ...path.value, id: stopping.value.id }, body: undefined })
    toast.add({ title: 'The link no longer works', color: 'success' })
    stopping.value = undefined
    await refresh()
  }
  catch (e) {
    toast.add({ title: 'Couldn\'t stop it', description: getErrorReasons(e)[0] ?? 'Try again.', color: 'error' })
  }
}
</script>

<template>
  <TenantAdminGate>
    <UDashboardPanel id="invites">
      <template #header>
        <AppNavbar title="Invite links" />
      </template>

      <template #body>
        <div class="max-w-2xl space-y-6">
          <p class="text-sm text-muted">
            Send a link to the people you want in. They sign in, or make an account, and use it.
            {{ settings?.requireApproval ? 'They then wait for you to let them in.' : 'They are in at once.' }}
          </p>

          <UPageCard
            title="Make a link"
            variant="subtle"
          >
            <UForm
              ref="form"
              :state="state"
              :validate="validate"
              class="space-y-4"
              @submit="submit"
            >
              <UAlert
                v-if="problem"
                :description="problem"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-alert"
              />

              <div class="grid gap-4 sm:grid-cols-2">
                <UFormField
                  label="They join as"
                  name="role"
                >
                  <USelect
                    v-model="state.role"
                    :items="roleItems"
                    class="w-full"
                    aria-label="They join as"
                  />
                </UFormField>

                <UFormField
                  label="In the unit"
                  name="unitId"
                >
                  <USelect
                    v-model="state.unit"
                    :items="unitItems"
                    class="w-full"
                    aria-label="In the unit"
                  />
                </UFormField>

                <UFormField
                  label="Works for (days)"
                  name="expiresInDays"
                >
                  <UInput
                    v-model.number="state.expiresInDays"
                    type="number"
                    min="1"
                    max="30"
                    class="w-full"
                  />
                </UFormField>

                <UFormField
                  label="People who can join"
                  name="maxUses"
                >
                  <UInput
                    v-model.number="state.maxUses"
                    type="number"
                    min="1"
                    max="100"
                    class="w-full"
                  />
                </UFormField>
              </div>

              <UButton
                type="submit"
                label="Make link"
                icon="i-lucide-link"
                :loading="creating"
              />
            </UForm>
          </UPageCard>

          <UAlert
            v-if="made"
            title="Here is the link"
            color="success"
            variant="subtle"
            icon="i-lucide-check"
            data-testid="new-link"
          >
            <template #description>
              <div class="space-y-3">
                <p>
                  It is shown only now, so copy it before you leave. If it is lost, make another and stop this one.
                </p>
                <div class="flex gap-2">
                  <UInput
                    :model-value="made.link"
                    class="flex-1"
                    readonly
                    aria-label="The link"
                    @focus="($event.target as HTMLInputElement).select()"
                  />
                  <UButton
                    v-if="isSupported"
                    :label="copied ? 'Copied' : 'Copy'"
                    :icon="copied ? 'i-lucide-check' : 'i-lucide-copy'"
                    color="neutral"
                    variant="subtle"
                    @click="copyLink"
                  />
                </div>
                <p class="text-xs">
                  Joins as {{ made.role.toLowerCase() }}{{ made.unit ? `, in ${made.unit}` : '' }}. Works until {{ df.format(made.expiresAt) }}, for up to {{ made.maxUses }} {{ made.maxUses === 1 ? 'person' : 'people' }}.
                </p>
              </div>
            </template>
          </UAlert>

          <UAlert
            v-if="error"
            title="Couldn't load the links"
            :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          />

          <section class="space-y-2">
            <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
              Links that were made
            </h2>

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
              v-else-if="!invites?.length && !error"
              class="py-4 text-center text-muted"
            >
              There are none yet.
            </p>

            <ul
              v-else
              class="divide-y divide-default rounded-lg border border-default"
            >
              <li
                v-for="invite in invites"
                :key="invite.id"
                class="flex flex-wrap items-center gap-3 p-3"
                :data-status="invite.status"
              >
                <div class="min-w-0 flex-1">
                  <p class="flex flex-wrap items-center gap-2 font-medium text-highlighted">
                    <span>{{ invite.role === 'Admin' ? 'Admin' : 'Member' }}{{ unitName(invite.unitId) ? `, ${unitName(invite.unitId)}` : '' }}</span>
                    <UBadge
                      :label="statusLabel[invite.status]"
                      :color="statusColor[invite.status]"
                      variant="subtle"
                      size="sm"
                    />
                  </p>
                  <p class="text-sm text-muted">
                    {{ invite.uses }} of {{ invite.maxUses }} joined · until {{ df.format(invite.expiresAt) }} · made {{ df.format(invite.createdAt) }}
                  </p>
                </div>

                <UButton
                  v-if="invite.status === 'Active'"
                  label="Stop"
                  color="error"
                  variant="ghost"
                  size="sm"
                  :aria-label="`Stop the link made ${df.format(invite.createdAt)}`"
                  @click="askStop(invite)"
                />
              </li>
            </ul>
          </section>
        </div>

        <UModal
          v-model:open="stoppingOpen"
          title="Stop this link?"
          description="Nobody can join with it any more. People who already did stay in. This can't be undone, but you can make another."
        >
          <template #footer>
            <div class="flex gap-2">
              <UButton
                label="Stop the link"
                color="error"
                :loading="revoking"
                @click="confirmStop"
              />
              <UButton
                label="Keep it"
                color="neutral"
                variant="ghost"
                @click="stopping = undefined"
              />
            </div>
          </template>
        </UModal>
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
