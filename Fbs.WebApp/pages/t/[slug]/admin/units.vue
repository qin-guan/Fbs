<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query'
import { useDeleteOrgUnitsById, useGetOrgUnits, usePostOrgUnits, usePutOrgUnitsById, type FbsWebApiEndpointsOrgUnitsUnitResponse } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Units' })

type Unit = FbsWebApiEndpointsOrgUnitsUnitResponse

const queryClient = useQueryClient()
const toast = useToast()
const { slug, isAdmin } = useTenant()
const path = computed(() => ({ slug: slug.value }))

const { data: units, isPending, error } = useGetOrgUnits({ path }, { query: { enabled: isAdmin } })
const sorted = computed(() => [...(units.value ?? [])].sort((a, b) => a.name.localeCompare(b.name)))

const refresh = () => invalidateUnder(queryClient, slug.value, 'Units', 'Facilities', 'Members')
const nameProblem = (name: string) => (name.trim().length < 1 || name.trim().length > 100 ? 'The name has to be between 1 and 100 characters.' : undefined)

// Adding
const { mutateAsync: create, isPending: creating } = usePostOrgUnits()
const newName = ref('')
const newProblem = ref<string>()
async function add() {
  newProblem.value = nameProblem(newName.value)
  if (newProblem.value) {
    return
  }

  try {
    await create({ path: path.value, body: { name: newName.value.trim() } })
    newName.value = ''
    await refresh()
  }
  catch (e) {
    newProblem.value = getFieldErrors(e)[0]?.message ?? getErrorReasons(e)[0] ?? 'Couldn\'t add it. Try again.'
  }
}

// Renaming, one at a time
const { mutateAsync: update, isPending: renaming } = usePutOrgUnitsById()
const renamingId = ref<string>()
const renameTo = ref('')
const renameProblem = ref<string>()
function startRename(unit: Unit) {
  renamingId.value = unit.id
  renameTo.value = unit.name
  renameProblem.value = undefined
}
async function rename() {
  if (!renamingId.value) {
    return
  }

  renameProblem.value = nameProblem(renameTo.value)
  if (renameProblem.value) {
    return
  }

  try {
    await update({ path: { ...path.value, id: renamingId.value }, body: { name: renameTo.value.trim() } })
    renamingId.value = undefined
    await refresh()
  }
  catch (e) {
    renameProblem.value = getFieldErrors(e)[0]?.message ?? getErrorReasons(e)[0] ?? 'Couldn\'t rename it. Try again.'
  }
}

// Deleting, which is only for a unit that nothing refers to
const { mutateAsync: remove, isPending: removing } = useDeleteOrgUnitsById()
const deleting = ref<Unit>()
const deletingOpen = computed({
  get: () => !!deleting.value,
  set: (open: boolean) => {
    if (!open) {
      deleting.value = undefined
    }
  },
})
const deleteProblem = ref<string>()
function askDelete(unit: Unit) {
  deleting.value = unit
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
    <UDashboardPanel id="units">
      <template #header>
        <AppNavbar title="Units">
          <template #trailing>
            <UBadge
              v-if="units"
              :label="units.length"
              variant="subtle"
            />
          </template>
        </AppNavbar>
      </template>

      <template #body>
        <div class="max-w-2xl space-y-6">
          <p class="text-sm text-muted">
            A platoon or a team. Membership decides which facilities those people can book, and they can change each other's bookings.
          </p>

          <UAlert
            v-if="error"
            title="Couldn't load the units"
            :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          />

          <UPageCard
            title="Add a unit"
            variant="subtle"
          >
            <form
              class="flex items-start gap-2"
              @submit.prevent="add"
            >
              <UFormField
                :error="newProblem"
                class="flex-1"
              >
                <UInput
                  v-model="newName"
                  class="w-full"
                  placeholder="Alpha Platoon"
                  aria-label="Name of the new unit"
                  @update:model-value="newProblem = undefined"
                />
              </UFormField>
              <UButton
                type="submit"
                label="Add"
                icon="i-lucide-plus"
                :loading="creating"
              />
            </form>
          </UPageCard>

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
            v-else-if="!sorted.length && !error"
            class="text-center text-muted"
          >
            There are no units yet, and you can leave it that way. People then have no unit, and a facility that isn't open to everyone can only be booked by an admin.
          </p>

          <ul
            v-else
            class="divide-y divide-default rounded-lg border border-default"
          >
            <li
              v-for="unit in sorted"
              :key="unit.id"
              class="flex items-start gap-2 p-3"
            >
              <form
                v-if="renamingId === unit.id"
                class="flex flex-1 items-start gap-2"
                @submit.prevent="rename"
              >
                <UFormField
                  :error="renameProblem"
                  class="flex-1"
                >
                  <UInput
                    v-model="renameTo"
                    class="w-full"
                    aria-label="New name"
                    autofocus
                    @update:model-value="renameProblem = undefined"
                  />
                </UFormField>
                <UButton
                  type="submit"
                  label="Save"
                  :loading="renaming"
                />
                <UButton
                  type="button"
                  label="Cancel"
                  color="neutral"
                  variant="ghost"
                  @click="renamingId = undefined"
                />
              </form>

              <template v-else>
                <span class="flex-1 self-center font-medium text-highlighted">{{ unit.name }}</span>
                <UButton
                  type="button"
                  icon="i-lucide-pencil"
                  color="neutral"
                  variant="ghost"
                  :aria-label="`Rename ${unit.name}`"
                  @click="startRename(unit)"
                />
                <UButton
                  type="button"
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  :aria-label="`Delete ${unit.name}`"
                  @click="askDelete(unit)"
                />
              </template>
            </li>
          </ul>
        </div>

        <UModal
          v-model:open="deletingOpen"
          :title="`Delete ${deleting?.name ?? 'the unit'}?`"
          description="It has to be empty first: no people in it, and no bookings."
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
