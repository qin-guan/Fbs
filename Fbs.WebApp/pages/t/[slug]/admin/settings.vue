<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { getOrgQueryKey, getOrgSettingsQueryKey, useGetOrgSettings, usePutOrgSettings } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Settings' })

const queryClient = useQueryClient()
const toast = useToast()
const { slug, path, isAdmin } = useTenant()

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
              description="It can't be changed, as links to it would stop working."
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
                description="Bookings are made, and shown, in this. The ones that are made stay at the same moment."
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
                description="For phone numbers written without one."
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
              description="Bookings start and end on this, such as on the hour or the half hour."
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
              description="People who join with a link wait until an admin lets them in. Otherwise they are in at once."
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
              description="People that were here before accounts can claim their place with the Telegram they had. Turn this off once they have. It can't be turned on again."
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
              Claiming a place from before accounts is off.
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
      </template>
    </UDashboardPanel>
  </TenantAdminGate>
</template>
