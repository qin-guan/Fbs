<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { getMeQueryKey, getOrgExport, getOrgQueryKey, getOrgSettingsQueryKey, useGetOrgSettings, usePostTenantsDeletion, usePutOrgSettings } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Settings' })

const queryClient = useQueryClient()
const toast = useToast()
const router = useRouter()
const { slug, path, isAdmin } = useTenant()
const { df } = useTenantFormatter()

const { data: settings, isPending, error } = useGetOrgSettings({ path: computed(() => ({ slug: slug.value })) }, { query: { enabled: isAdmin } })
const timeZones = knownTimeZones()
const slotOptions = [
  { label: '15 minutes', value: 15 },
  { label: '30 minutes', value: 30 },
  { label: '1 hour', value: 60 },
]

const state = reactive({
  name: '',
  timeZone: 'UTC',
  defaultCountryCode: '',
  slotMinutes: 30,
  requireApproval: false,
  stopClaims: false,
})

// What the form starts as, and goes back to once it is saved
function reset() {
  if (settings.value) {
    Object.assign(state, { ...settings.value, stopClaims: false })
  }
}
watch(settings, reset, { immediate: true })

const dirty = computed(() => !!settings.value && (
  state.name !== settings.value.name
  || state.timeZone !== settings.value.timeZone
  || state.defaultCountryCode !== settings.value.defaultCountryCode
  || state.slotMinutes !== settings.value.slotMinutes
  || state.requireApproval !== settings.value.requireApproval
  || state.stopClaims
))

const form = useTemplateRef('form')
function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (values.name.trim().length < 2 || values.name.trim().length > 100) {
    errors.push({ name: 'name', message: 'The name has to be between 2 and 100 characters.' })
  }

  if (!/^[0-9]{1,4}$/.test(values.defaultCountryCode)) {
    errors.push({ name: 'defaultCountryCode', message: 'The calling code is 1 to 4 digits, without a plus.' })
  }

  return errors
}

const { mutateAsync: save, isPending: saving } = usePutOrgSettings()

// A copy of everything of the organization, which is its own to have
const downloading = ref(false)
async function downloadCopy() {
  downloading.value = true
  try {
    downloadJson(`${slug.value}-data.json`, await getOrgExport({ path: { slug: slug.value } }).unwrap())
  }
  catch (e) {
    toast.add({
      title: getErrorStatus(e) === 429 ? 'Too many tries' : 'Couldn\'t get the copy',
      description: getErrorStatus(e) === 429 ? 'Wait a little, then try again.' : 'Try again in a moment.',
      color: 'error',
    })
  }
  finally {
    downloading.value = false
  }
}

// Deleting it, which is asked for by typing its address, and is not for good until some days later
const deleting = ref(false)
const confirmation = ref('')
const deleteProblem = ref<string>()
const { mutateAsync: requestDeletion, isPending: requestingDeletion } = usePostTenantsDeletion()
function askToDelete() {
  confirmation.value = ''
  deleteProblem.value = undefined
  deleting.value = true
}
function keepIt() {
  deleting.value = false
}

async function confirmDeletion() {
  deleteProblem.value = undefined
  try {
    const { deleteAfter } = await requestDeletion({ path: { slug: slug.value }, body: { confirm: confirmation.value } })
    // It can't be used from now, and what was known of it is not right any more
    await queryClient.invalidateQueries({ queryKey: getMeQueryKey() })
    await queryClient.invalidateQueries({ queryKey: getOrgQueryKey({ path: { slug: slug.value } }) })
    deleting.value = false
    toast.add({
      title: 'The organization is to be deleted',
      description: deleteAfter ? `On ${df.value.format(deleteAfter)} at the earliest. Until then any admin can restore it.` : 'Until then any admin can restore it.',
      color: 'success',
    })
    await router.push('/orgs')
  }
  catch (e) {
    deleteProblem.value = getFieldErrors(e)[0]?.message ?? getErrorReasons(e)[0] ?? 'Couldn\'t do that. Try again.'
  }
}

async function submit() {
  try {
    const saved = await save({
      path: { slug: slug.value },
      body: {
        name: state.name.trim(),
        timeZone: state.timeZone,
        defaultCountryCode: state.defaultCountryCode.trim(),
        slotMinutes: state.slotMinutes,
        requireApproval: state.requireApproval,
        // Left out unless it is being turned off, as that is all that can be done to it
        legacyClaimEnabled: state.stopClaims ? false : undefined,
      },
    })
    queryClient.setQueryData(getOrgSettingsQueryKey({ path: { slug: slug.value } }), saved)
    await queryClient.invalidateQueries({ queryKey: getOrgQueryKey({ path: { slug: slug.value } }) })
    reset()
    toast.add({ title: 'Saved', color: 'success' })
  }
  catch (e) {
    const fields = getFieldErrors(e)
    if (fields.length) {
      form.value?.setErrors(fields)
    }
    else {
      toast.add({ title: 'Couldn\'t save', description: getErrorReasons(e)[0] ?? 'Something went wrong. Try again.', color: 'error' })
    }
  }
}
</script>

<template>
  <TenantAdminGate>
    <UDashboardPanel id="settings">
      <template #header>
        <AppNavbar title="Settings" />
      </template>

      <template #body>
        <UAlert
          v-if="error"
          title="Couldn't load the settings"
          :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
        />

        <div
          v-else-if="isPending"
          class="flex flex-1 items-center justify-center text-muted"
        >
          <UIcon
            name="i-lucide-loader-circle"
            class="size-6 animate-spin"
          />
        </div>

        <UForm
          v-else
          ref="form"
          :state="state"
          :validate="validate"
          class="max-w-2xl space-y-6"
          @submit="submit"
        >
          <UPageCard
            title="The organization"
            variant="subtle"
          >
            <UFormField
              label="Name"
              name="name"
              required
            >
              <UInput
                v-model="state.name"
                class="w-full"
                autocomplete="organization"
              />
            </UFormField>

            <UFormField
              label="Address"
              description="This can't be changed. Links to the organization would stop working."
            >
              <UInput
                :model-value="path()"
                class="w-full"
                readonly
                disabled
              />
            </UFormField>

            <div class="grid gap-4 sm:grid-cols-2">
              <UFormField
                label="Time zone"
                name="timeZone"
                description="Bookings are made and shown in this time zone. Existing bookings stay at the same moment."
              >
                <USelectMenu
                  v-model="state.timeZone"
                  :items="timeZones"
                  class="w-full"
                />
              </UFormField>

              <UFormField
                label="Calling code"
                name="defaultCountryCode"
                description="Used when a phone number is entered without one."
              >
                <UInput
                  v-model="state.defaultCountryCode"
                  class="w-full"
                  inputmode="numeric"
                >
                  <template #leading>
                    <span class="text-muted">+</span>
                  </template>
                </UInput>
              </UFormField>
            </div>
          </UPageCard>

          <UPageCard
            title="Bookings and people"
            variant="subtle"
          >
            <UFormField
              label="Shortest booking"
              name="slotMinutes"
              description="Bookings start and end on this interval, such as the hour or the half hour."
            >
              <USelect
                v-model="state.slotMinutes"
                :items="slotOptions"
                class="w-full sm:w-56"
                aria-label="Shortest booking"
              />
            </UFormField>

            <UFormField
              name="requireApproval"
              label="An admin lets people in"
              description="People who join with a link wait until you let them in. If this is off, they join straight away."
            >
              <USwitch
                v-model="state.requireApproval"
                aria-label="An admin lets people in"
              />
            </UFormField>

            <UFormField
              v-if="settings?.legacyClaimEnabled"
              name="stopClaims"
              label="Stop people claiming their place from before"
              description="People who were here before accounts can claim their place with the Telegram they used. Turn this off once they have. You can't turn it back on."
            >
              <USwitch
                v-model="state.stopClaims"
                aria-label="Stop people claiming their place from before"
              />
            </UFormField>
            <p
              v-else
              class="text-sm text-muted"
            >
              People can no longer claim a place from before accounts.
            </p>
          </UPageCard>

          <div class="flex items-center gap-2">
            <UButton
              type="submit"
              label="Save"
              icon="i-lucide-save"
              :loading="saving"
              :disabled="!dirty"
            />
            <UButton
              type="button"
              label="Undo"
              color="neutral"
              variant="ghost"
              :disabled="!dirty || saving"
              @click="reset(); form?.clear()"
            />
          </div>
        </UForm>

        <div
          v-if="settings"
          class="max-w-2xl space-y-6"
        >
          <TenantCalendarConnection />

          <UPageCard
            title="A copy of the data"
            description="A file of this organization: who is in it, their phone numbers, every booking, and the history. Downloading a copy is recorded in the history."
            variant="subtle"
            icon="i-lucide-download"
          >
            <UButton
              label="Download a copy"
              icon="i-lucide-download"
              color="neutral"
              variant="subtle"
              :loading="downloading"
              @click="downloadCopy"
            />
          </UPageCard>

          <UPageCard
            title="Delete this organization"
            description="After you delete it, nobody can use it and its invite links stop working. It is deleted for good some days later, along with everything in it. Any admin can restore it until then."
            variant="subtle"
            icon="i-lucide-trash-2"
          >
            <UButton
              label="Delete this organization"
              icon="i-lucide-trash-2"
              color="error"
              variant="subtle"
              @click="askToDelete"
            />
          </UPageCard>
        </div>

        <UModal
          v-model:open="deleting"
          title="Delete this organization?"
          description="Nobody can use it from now. It is deleted for good after a while. Any admin can restore it until then."
        >
          <template #body>
            <form
              class="space-y-4"
              @submit.prevent="confirmDeletion"
            >
              <UAlert
                v-if="deleteProblem"
                :description="deleteProblem"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-alert"
              />
              <UFormField
                :label="`Type ${slug} to confirm`"
                name="confirm"
              >
                <UInput
                  v-model="confirmation"
                  class="w-full"
                  autocomplete="off"
                  aria-label="The address of the organization"
                />
              </UFormField>
              <div class="flex gap-2">
                <UButton
                  type="submit"
                  label="Delete the organization"
                  color="error"
                  :disabled="confirmation !== slug"
                  :loading="requestingDeletion"
                />
                <UButton
                  type="button"
                  label="Keep it"
                  color="neutral"
                  variant="ghost"
                  @click="keepIt"
                />
              </div>
            </form>
          </template>
        </UModal>
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
