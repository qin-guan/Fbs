<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { useGetOrgMembers, useGetOrgUnits, usePostOrgMembers, usePutOrgMembersById, type FbsWebApiEndpointsOrgMembersMemberResponse } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'People' })

type Member = FbsWebApiEndpointsOrgMembersMemberResponse
type Role = Member['role']
type Scope = Member['notificationScope']

const queryClient = useQueryClient()
const toast = useToast()
const { slug, isAdmin } = useTenant()
const path = computed(() => ({ slug: slug.value }))

const showRemoved = ref(false)
const search = ref('')
const { data: members, isPending, error } = useGetOrgMembers(
  { path, query: computed(() => ({ includeRemoved: showRemoved.value })) },
  { query: { enabled: isAdmin } },
)
const { data: units } = useGetOrgUnits({ path }, { query: { enabled: isAdmin } })

const unitName = (id?: string | null) => (id ? units.value?.find(u => u.id === id)?.name : undefined)
const NO_UNIT = 'none'
const unitItems = computed(() => [{ label: 'No unit', value: NO_UNIT }, ...[...(units.value ?? [])].sort((a, b) => a.name.localeCompare(b.name)).map(u => ({ label: u.name, value: u.id }))])
const roleItems: Array<{ label: string, value: Role }> = [{ label: 'Member', value: 'Member' }, { label: 'Admin', value: 'Admin' }]
const scopeItems: Array<{ label: string, value: Scope }> = [
  { label: 'Nothing', value: 'None' },
  { label: 'Bookings of their unit', value: 'Unit' },
  { label: 'Every booking', value: 'All' },
]

const matching = computed(() => {
  const words = search.value.trim().toLowerCase()
  return (members.value ?? []).filter(m => !words || [m.displayName, m.phone, unitName(m.unitId)].some(v => (v ?? '').toLowerCase().includes(words)))
})
const waiting = computed(() => matching.value.filter(m => m.status === 'Pending'))
const others = computed(() => matching.value.filter(m => m.status !== 'Pending'))
const waitingCount = computed(() => (members.value ?? []).filter(m => m.status === 'Pending').length)

const statusColor = { Active: 'success', Pending: 'warning', Unclaimed: 'neutral', Removed: 'error' } as const
const statusHint: Record<Member['status'], string> = {
  Active: 'Signed in',
  Pending: 'Waiting to be let in',
  Unclaimed: 'Added by phone number, and belongs once they sign in',
  Removed: 'Removed',
}

const refresh = () => invalidateUnder(queryClient, slug.value, 'Members', 'Units')

// What is sent to change somebody is all of it, and this is what it is now with some of it changed
const bodyOf = (m: Member, changes: { membership?: 'In' | 'Removed' } = {}) => ({
  displayName: m.displayName,
  phone: m.phone ?? '',
  unitId: m.unitId ?? null,
  role: m.role,
  notificationScope: m.notificationScope,
  membership: 'In' as 'In' | 'Removed',
  ...changes,
})

const { mutateAsync: update, isPending: updating } = usePutOrgMembersById()
const { mutateAsync: create, isPending: creating } = usePostOrgMembers()
const busy = ref<string>()

/** Lets somebody in, turns them away, removes them or lets them back in: what else is about them stays. */
async function setMembership(member: Member, membership: 'In' | 'Removed', done: string) {
  busy.value = member.id
  try {
    await update({ path: { ...path.value, id: member.id }, body: bodyOf(member, { membership }) })
    toast.add({ title: done, color: 'success' })
    removing.value = undefined
    await refresh()
  }
  catch (e) {
    const reason = getErrorReasons(e)[0] ?? 'Something went wrong. Try again.'
    if (removing.value) {
      removeProblem.value = reason
    }
    else {
      toast.add({ title: 'Couldn\'t do that', description: reason, color: 'error' })
    }
  }
  finally {
    busy.value = undefined
  }
}

// Removing asks first
const removing = ref<Member>()
const removingOpen = computed({
  get: () => !!removing.value,
  set: (open: boolean) => {
    if (!open) {
      removing.value = undefined
    }
  },
})
const removeProblem = ref<string>()
function askRemove(member: Member) {
  removing.value = member
  removeProblem.value = undefined
}
async function confirmRemove() {
  if (removing.value) {
    await setMembership(removing.value, 'Removed', `${removing.value.displayName} removed`)
  }
}

// Adding somebody by phone, and changing somebody, in the same form
const form = useTemplateRef('form')
const editing = ref(false)
const editingMember = ref<Member>()
const formProblem = ref<string>()
const state = reactive({ displayName: '', phone: '', unit: NO_UNIT, role: 'Member' as Role, notificationScope: 'None' as Scope })

function startAdd() {
  editingMember.value = undefined
  formProblem.value = undefined
  Object.assign(state, { displayName: '', phone: '', unit: NO_UNIT, role: 'Member', notificationScope: 'None' })
  editing.value = true
}

function startEdit(member: Member) {
  editingMember.value = member
  formProblem.value = undefined
  Object.assign(state, { displayName: member.displayName, phone: member.phone ?? '', unit: member.unitId ?? NO_UNIT, role: member.role, notificationScope: member.notificationScope })
  editing.value = true
}

function closeForm() {
  editing.value = false
}

// A person who hasn't signed in is found by their phone number, so it can't be left out
const phoneRequired = computed(() => !editingMember.value || editingMember.value.status === 'Unclaimed')

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (values.displayName.trim().length < 1 || values.displayName.trim().length > 200) {
    errors.push({ name: 'displayName', message: 'The name has to be between 1 and 200 characters.' })
  }

  if (phoneRequired.value && !values.phone.trim()) {
    errors.push({ name: 'phone', message: 'A phone number is needed, as it is how they are found when they sign in.' })
  }

  return errors
}

const fields = new Set(['displayName', 'phone', 'unitId', 'role', 'notificationScope'])
async function submit() {
  formProblem.value = undefined
  const body = {
    displayName: state.displayName.trim(),
    phone: state.phone.trim(),
    unitId: state.unit === NO_UNIT ? null : state.unit,
    role: state.role,
    notificationScope: state.notificationScope,
  }
  try {
    if (editingMember.value) {
      await update({ path: { ...path.value, id: editingMember.value.id }, body: { ...body, membership: editingMember.value.status === 'Removed' ? 'Removed' : 'In' } })
    }
    else {
      await create({ path: path.value, body })
    }

    editing.value = false
    await refresh()
  }
  catch (e) {
    const problems = getFieldErrors(e)
    const onFields = problems.filter(p => fields.has(String(p.name)))
    if (onFields.length) {
      form.value?.setErrors(onFields)
    }

    // What isn't about a field, such as the last admin not being made a member
    const general = problems.filter(p => !fields.has(String(p.name))).map(p => p.message)
    if (general.length || !onFields.length) {
      formProblem.value = general[0] ?? 'Something went wrong. Try again.'
    }
  }
}

const initials = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]!.toUpperCase()).join('')
</script>

<template>
  <TenantAdminGate>
    <UDashboardPanel id="people">
      <template #header>
        <AppNavbar title="People">
          <template #trailing>
            <UBadge
              v-if="members"
              :label="members.length"
              variant="subtle"
            />
          </template>

          <template #right>
            <UButton
              icon="i-lucide-user-plus"
              label="Add"
              @click="startAdd"
            />
          </template>
        </AppNavbar>
      </template>

      <template #body>
        <div class="max-w-3xl space-y-4">
          <UAlert
            v-if="waitingCount"
            :title="waitingCount === 1 ? 'Somebody is waiting to be let in' : `${waitingCount} people are waiting to be let in`"
            description="They joined with a link. Until you let them in, they can't see or book anything."
            color="warning"
            variant="subtle"
            icon="i-lucide-hourglass"
          />

          <UAlert
            v-if="error"
            title="Couldn't load the people"
            :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          />

          <div class="flex flex-wrap items-center gap-3">
            <UInput
              v-model="search"
              icon="i-lucide-search"
              placeholder="Name, phone or unit"
              aria-label="Search people"
              class="w-full sm:w-64"
            />
            <USwitch
              v-model="showRemoved"
              label="Show removed"
            />
          </div>

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
            v-else-if="!matching.length && !error"
            class="py-6 text-center text-muted"
          >
            {{ search.trim() ? 'Nobody matches.' : 'There is nobody yet. Send an invite link, or add somebody by their phone number.' }}
          </p>

          <ul
            v-if="matching.length"
            class="divide-y divide-default rounded-lg border border-default"
          >
            <li
              v-for="member in [...waiting, ...others]"
              :key="member.id"
              class="flex flex-wrap items-center gap-3 p-3"
              :data-status="member.status"
            >
              <UAvatar
                :text="initials(member.displayName)"
                size="md"
                aria-hidden="true"
              />

              <div class="min-w-0 flex-1">
                <p class="flex flex-wrap items-center gap-2 font-medium text-highlighted">
                  <span
                    class="truncate"
                    data-testid="member-name"
                  >{{ member.displayName }}</span>
                  <UBadge
                    :label="member.status"
                    :color="statusColor[member.status]"
                    variant="subtle"
                    size="sm"
                    :title="statusHint[member.status]"
                  />
                  <UBadge
                    v-if="member.role === 'Admin'"
                    label="Admin"
                    color="primary"
                    variant="subtle"
                    size="sm"
                  />
                </p>
                <p class="truncate text-sm text-muted">
                  {{ [member.phone, unitName(member.unitId)].filter(Boolean).join(' · ') || 'No phone number, no unit' }}
                </p>
              </div>

              <div class="flex items-center gap-1">
                <template v-if="member.status === 'Pending'">
                  <UButton
                    label="Let in"
                    size="sm"
                    :loading="busy === member.id && updating"
                    :aria-label="`Let ${member.displayName} in`"
                    @click="setMembership(member, 'In', `${member.displayName} is in`)"
                  />
                  <UButton
                    label="Turn away"
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    :aria-label="`Turn ${member.displayName} away`"
                    @click="askRemove(member)"
                  />
                </template>

                <template v-else-if="member.status === 'Removed'">
                  <UButton
                    label="Let back in"
                    size="sm"
                    color="neutral"
                    variant="subtle"
                    :loading="busy === member.id && updating"
                    :aria-label="`Let ${member.displayName} back in`"
                    @click="setMembership(member, 'In', `${member.displayName} is back in`)"
                  />
                </template>

                <template v-else>
                  <UButton
                    icon="i-lucide-pencil"
                    color="neutral"
                    variant="ghost"
                    :aria-label="`Change ${member.displayName}`"
                    @click="startEdit(member)"
                  />
                  <UButton
                    icon="i-lucide-user-x"
                    color="error"
                    variant="ghost"
                    :aria-label="`Remove ${member.displayName}`"
                    @click="askRemove(member)"
                  />
                </template>
              </div>
            </li>
          </ul>
        </div>

        <UModal
          v-model:open="editing"
          :title="editingMember ? `Change ${editingMember.displayName}` : 'Add somebody'"
          :description="editingMember ? undefined : 'They are found by their phone number when they sign in, and are in from then. To have them join themselves, send an invite link instead.'"
        >
          <template #body>
            <UForm
              ref="form"
              :state="state"
              :validate="validate"
              class="space-y-4"
              @submit="submit"
            >
              <UAlert
                v-if="formProblem"
                :description="formProblem"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-alert"
              />

              <UFormField
                label="Name"
                name="displayName"
                required
              >
                <UInput
                  v-model="state.displayName"
                  class="w-full"
                  placeholder="CPT Sam Tan"
                />
              </UFormField>

              <UFormField
                label="Phone number"
                name="phone"
                :required="phoneRequired"
                description="With a + and the country code, or in the organization's country."
              >
                <UInput
                  v-model="state.phone"
                  class="w-full"
                  inputmode="tel"
                  placeholder="9123 4567"
                />
              </UFormField>

              <div class="grid gap-4 sm:grid-cols-2">
                <UFormField
                  label="Unit"
                  name="unitId"
                >
                  <USelect
                    v-model="state.unit"
                    :items="unitItems"
                    class="w-full"
                    aria-label="Unit"
                  />
                </UFormField>

                <UFormField
                  label="Role"
                  name="role"
                >
                  <USelect
                    v-model="state.role"
                    :items="roleItems"
                    class="w-full"
                    aria-label="Role"
                  />
                </UFormField>
              </div>

              <UFormField
                label="Tell them about"
                name="notificationScope"
                description="On Telegram, once they have linked it."
              >
                <USelect
                  v-model="state.notificationScope"
                  :items="scopeItems"
                  class="w-full"
                  aria-label="Tell them about"
                />
              </UFormField>

              <div class="flex gap-2">
                <UButton
                  type="submit"
                  :label="editingMember ? 'Save' : 'Add'"
                  :loading="creating || updating"
                />
                <UButton
                  type="button"
                  label="Cancel"
                  color="neutral"
                  variant="ghost"
                  @click="closeForm"
                />
              </div>
            </UForm>
          </template>
        </UModal>

        <UModal
          v-model:open="removingOpen"
          :title="removing?.status === 'Pending' ? `Turn ${removing.displayName} away?` : `Remove ${removing?.displayName ?? 'them'}?`"
          :description="removing?.status === 'Pending' ? 'They are told nothing, and can ask again with a link.' : 'They can\'t use the organization any more. What they booked stays as it is, and they can be let back in.'"
        >
          <template #body>
            <UAlert
              v-if="removeProblem"
              :description="removeProblem"
              color="error"
              variant="subtle"
              icon="i-lucide-circle-alert"
            />
          </template>
          <template #footer>
            <div class="flex gap-2">
              <UButton
                :label="removing?.status === 'Pending' ? 'Turn away' : 'Remove'"
                color="error"
                :loading="updating"
                @click="confirmRemove"
              />
              <UButton
                label="Keep them"
                color="neutral"
                variant="ghost"
                @click="removing = undefined"
              />
            </div>
          </template>
        </UModal>
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
