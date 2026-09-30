<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { useDeleteOrgFacilitiesById, useGetOrgFacilities, useGetOrgUnits, usePostOrgFacilities, usePutOrgFacilitiesById, type FbsWebApiEndpointsOrgFacilitiesFacilityResponse } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Facilities' })

type Facility = FbsWebApiEndpointsOrgFacilitiesFacilityResponse

const queryClient = useQueryClient()
const toast = useToast()
const { slug, isAdmin } = useTenant()
const path = computed(() => ({ slug: slug.value }))

const { data: facilities, isPending, error } = useGetOrgFacilities({ path }, { query: { enabled: isAdmin } })
const { data: units } = useGetOrgUnits({ path }, { query: { enabled: isAdmin } })

const unitItems = computed(() => [...(units.value ?? [])].sort((a, b) => a.name.localeCompare(b.name)).map(u => ({ label: u.name, value: u.id })))
const unitName = (id: string) => units.value?.find(u => u.id === id)?.name

const sections = computed(() => {
  const groups = new Map<string, Facility[]>()
  for (const facility of [...(facilities.value ?? [])].sort((a, b) => a.name.localeCompare(b.name))) {
    const group = facility.group?.trim() || 'Other'
    groups.set(group, [...(groups.get(group) ?? []), facility])
  }

  return [...groups].sort(([a], [b]) => (a === 'Other' ? 1 : b === 'Other' ? -1 : a.localeCompare(b))).map(([group, rows]) => ({ group, rows }))
})
const groupNames = computed(() => [...new Set((facilities.value ?? []).flatMap(f => (f.group?.trim() ? [f.group.trim()] : [])))])

function whoCanBook(facility: Facility) {
  if (facility.availableToAll) {
    return 'Everyone'
  }

  const names = facility.unitIds.flatMap(id => unitName(id) ?? [])
  return names.length ? names.join(', ') : 'Admins only'
}

const refresh = () => invalidateUnder(queryClient, slug.value, 'Facilities')

// Adding and changing, in the same form
const { mutateAsync: create, isPending: creating } = usePostOrgFacilities()
const { mutateAsync: update, isPending: updating } = usePutOrgFacilitiesById()
const saving = computed(() => creating.value || updating.value)

const form = useTemplateRef('form')
const formProblem = ref<string>()
const editing = ref(false)
const editingId = ref<string>()
const state = reactive({ name: '', group: '', availableToAll: true, unitIds: [] as string[] })

function startAdd() {
  editingId.value = undefined
  formProblem.value = undefined
  Object.assign(state, { name: '', group: '', availableToAll: true, unitIds: [] })
  editing.value = true
}

function startEdit(facility: Facility) {
  editingId.value = facility.id
  formProblem.value = undefined
  Object.assign(state, { name: facility.name, group: facility.group ?? '', availableToAll: facility.availableToAll, unitIds: [...facility.unitIds] })
  editing.value = true
}

const fields = new Set(['name', 'group', 'availableToAll', 'unitIds'])

function closeForm() {
  editing.value = false
}

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (values.name.trim().length < 1 || values.name.trim().length > 100) {
    errors.push({ name: 'name', message: 'The name has to be between 1 and 100 characters.' })
  }

  if (values.group.trim().length > 100) {
    errors.push({ name: 'group', message: 'The group can be up to 100 characters.' })
  }

  return errors
}

async function submit() {
  formProblem.value = undefined
  const body = {
    name: state.name.trim(),
    group: state.group.trim() || null,
    availableToAll: state.availableToAll,
    // Kept apart from a facility that is for everyone, which the API doesn't take
    unitIds: state.availableToAll ? [] : state.unitIds,
  }
  try {
    if (editingId.value) {
      await update({ path: { ...path.value, id: editingId.value }, body })
    }
    else {
      await create({ path: path.value, body })
    }

    editing.value = false
    await refresh()
  }
  catch (e) {
    // What is about a field is shown by it, and what is not, such as there being as many facilities as there can be, in the form
    const problems = getFieldErrors(e)
    const onFields = problems.filter(p => fields.has(String(p.name)))
    if (onFields.length) {
      form.value?.setErrors(onFields)
    }

    if (onFields.length < problems.length || !problems.length) {
      formProblem.value = problems.find(p => !fields.has(String(p.name)))?.message ?? getErrorReasons(e)[0] ?? 'Something went wrong. Try again.'
    }
  }
}

// Deleting, which is only for a facility that nothing has been booked on
const { mutateAsync: remove, isPending: removing } = useDeleteOrgFacilitiesById()
const deleting = ref<Facility>()
const deletingOpen = computed({
  get: () => !!deleting.value,
  set: (open: boolean) => {
    if (!open) {
      deleting.value = undefined
    }
  },
})
const deleteProblem = ref<string>()
function askDelete(facility: Facility) {
  deleting.value = facility
  deleteProblem.value = undefined
}
async function confirmDelete() {
  if (!deleting.value) {
    return
  }

  try {
    await remove({ path: { ...path.value, id: deleting.value.id }, body: undefined })
    toast.add({ title: `${deleting.value.name} deleted`, color: 'success' })
    deleting.value = undefined
    await refresh()
  }
  catch (e) {
    deleteProblem.value = getErrorReasons(e)[0] ?? 'Couldn\'t delete it. Try again.'
  }
}
</script>

<template>
  <TenantAdminGate>
    <UDashboardPanel id="facilities">
      <template #header>
        <AppNavbar title="Facilities">
          <template #trailing>
            <UBadge
              v-if="facilities"
              :label="facilities.length"
              variant="subtle"
            />
          </template>

          <template #right>
            <UButton
              icon="i-lucide-plus"
              label="Add"
              @click="startAdd"
            />
          </template>
        </AppNavbar>
      </template>

      <template #body>
        <div class="max-w-3xl space-y-6">
          <p class="text-sm text-muted">
            What people can book. A facility is for everyone, or for the units it is given to; admins can book any.
          </p>

          <UAlert
            v-if="error"
            title="Couldn't load the facilities"
            :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          />

          <div
            v-if="isPending"
            class="flex justify-center py-6 text-muted"
          >
            <UIcon
              name="i-lucide-loader-circle"
              class="size-6 animate-spin"
            />
          </div>

          <div
            v-else-if="!sections.length && !error"
            class="flex flex-col items-center gap-3 py-6 text-center text-muted"
          >
            <UIcon
              name="i-lucide-building-2"
              class="size-8"
            />
            <p>Nothing can be booked until there is a facility.</p>
            <UButton
              label="Add the first"
              icon="i-lucide-plus"
              @click="startAdd"
            />
          </div>

          <section
            v-for="section in sections"
            :key="section.group"
            class="space-y-2"
          >
            <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
              {{ section.group }}
            </h2>
            <ul class="divide-y divide-default rounded-lg border border-default">
              <li
                v-for="facility in section.rows"
                :key="facility.id"
                class="flex items-center gap-2 p-3"
              >
                <div class="min-w-0 flex-1">
                  <p class="truncate font-medium text-highlighted">
                    {{ facility.name }}
                  </p>
                  <p class="truncate text-sm text-muted">
                    Can be booked by: {{ whoCanBook(facility) }}
                  </p>
                </div>
                <UButton
                  type="button"
                  icon="i-lucide-pencil"
                  color="neutral"
                  variant="ghost"
                  :aria-label="`Change ${facility.name}`"
                  @click="startEdit(facility)"
                />
                <UButton
                  type="button"
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  :aria-label="`Delete ${facility.name}`"
                  @click="askDelete(facility)"
                />
              </li>
            </ul>
          </section>
        </div>

        <UModal
          v-model:open="editing"
          :title="editingId ? 'Change the facility' : 'Add a facility'"
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
                name="name"
                required
              >
                <UInput
                  v-model="state.name"
                  class="w-full"
                  placeholder="Multi-purpose hall"
                />
              </UFormField>

              <UFormField
                label="Group"
                name="group"
                description="What kind it is, to have them together in the lists. Leave it empty for none."
              >
                <UInput
                  v-model="state.group"
                  class="w-full"
                  list="facility-groups"
                  placeholder="Indoor"
                />
                <datalist id="facility-groups">
                  <option
                    v-for="group in groupNames"
                    :key="group"
                    :value="group"
                  />
                </datalist>
              </UFormField>

              <UFormField
                name="availableToAll"
                label="Everyone can book it"
                description="If not, only people in the units you pick can, and admins."
              >
                <USwitch
                  v-model="state.availableToAll"
                  aria-label="Everyone can book it"
                />
              </UFormField>

              <UFormField
                v-if="!state.availableToAll"
                label="Units that can book it"
                name="unitIds"
                :description="unitItems.length ? undefined : 'There are no units yet, so only admins can book it. Add units first to give it to them.'"
              >
                <USelectMenu
                  v-model="state.unitIds"
                  :items="unitItems"
                  value-key="value"
                  multiple
                  class="w-full"
                  placeholder="No unit"
                  aria-label="Units that can book it"
                />
              </UFormField>

              <div class="flex gap-2">
                <UButton
                  type="submit"
                  :label="editingId ? 'Save' : 'Add'"
                  :loading="saving"
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
          v-model:open="deletingOpen"
          :title="`Delete ${deleting?.name ?? 'the facility'}?`"
          description="It can be deleted when nothing has been booked on it. Otherwise it stays, so what was booked is kept."
        >
          <template #body>
            <UAlert
              v-if="deleteProblem"
              :description="deleteProblem"
              color="error"
              variant="subtle"
              icon="i-lucide-circle-alert"
            />
          </template>
          <template #footer>
            <div class="flex gap-2">
              <UButton
                label="Delete"
                color="error"
                :loading="removing"
                @click="confirmDelete"
              />
              <UButton
                label="Keep it"
                color="neutral"
                variant="ghost"
                @click="deleting = undefined"
              />
            </div>
          </template>
        </UModal>
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
