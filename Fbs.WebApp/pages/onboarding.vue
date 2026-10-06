<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQueryClient } from '@tanstack/vue-query'
import { getMeQueryKey, usePostTenants } from '~/api'

definePageMeta({
  layout: 'account',
})

useHead({ title: 'Get started' })

const queryClient = useQueryClient()
const router = useRouter()
const toast = useToast()
const lastOrganization = useLocalStorage<string | null>('fbs:last-organization', null)

const timeZones = knownTimeZones()
const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
const state = reactive({
  name: '',
  slug: '',
  timeZone: timeZones.includes(browserTimeZone) ? browserTimeZone : 'UTC',
  defaultCountryCode: callingCodeFor(browserTimeZone),
})

// The address follows the name until somebody edits it
const slugEdited = ref(false)
watch(() => state.name, (name) => {
  if (!slugEdited.value) {
    state.slug = slugify(name)
  }
})

const serverErrors = ref<FormError[]>([])
const limitReached = ref(false)

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  if (values.name.trim().length < 2) {
    errors.push({ name: 'name', message: 'Give it a name of at least 2 characters.' })
  }

  if (!isValidSlug(values.slug)) {
    errors.push({ name: 'slug', message: 'Use 3 to 63 lower case letters, digits and hyphens, starting and ending with a letter or digit.' })
  }

  if (!/^[0-9]{1,4}$/.test(values.defaultCountryCode)) {
    errors.push({ name: 'defaultCountryCode', message: 'The calling code is 1 to 4 digits, without a plus.' })
  }

  return [...errors, ...serverErrors.value]
}

const { mutateAsync: create, isPending: creating } = usePostTenants()

async function submit() {
  serverErrors.value = []
  limitReached.value = false
  try {
    const made = await create({ body: { name: state.name.trim(), slug: state.slug, timeZone: state.timeZone, defaultCountryCode: state.defaultCountryCode } })
    await queryClient.invalidateQueries({ queryKey: getMeQueryKey() })
    lastOrganization.value = made.slug
    await router.push(`/t/${made.slug}`)
  }
  catch (error) {
    const status = getErrorStatus(error)
    const codes = getErrorCodes(error)
    if (status === 409) {
      serverErrors.value = [{ name: 'slug', message: 'That address is taken. Try another.' }]
    }
    else if (codes.includes('slug-reserved')) {
      serverErrors.value = [{ name: 'slug', message: 'That address can\'t be used. Try another.' }]
    }
    else if (codes.includes('tenant-limit')) {
      limitReached.value = true
    }
    else if (status === 429) {
      toast.add({ title: 'Too many tries', description: 'Wait a little, then try again.', color: 'warning' })
    }
    else {
      toast.add({ title: 'Couldn\'t make it', description: getErrorReasons(error)[0] ?? 'Something went wrong. Try again.', color: 'error' })
    }
  }
}

const pasted = ref('')
const pastedToken = computed(() => tokenFromInvite(pasted.value))
async function join() {
  if (pastedToken.value) {
    await router.push(`/join/${pastedToken.value}`)
  }
}
</script>

<template>
  <div class="space-y-6">
    <UPageCard
      title="Make an organization"
      description="For your unit, club, or team. You'll be its admin, and you can invite the others with a link."
      variant="subtle"
    >
      <UAlert
        v-if="limitReached"
        title="You have made as many organizations as you can"
        description="Ask an admin of one of them to make you an admin, or join with a link."
        color="warning"
        variant="subtle"
        icon="i-lucide-triangle-alert"
      />

      <UForm
        :state="state"
        :validate="validate"
        class="space-y-4"
        @submit="submit"
      >
        <UFormField
          label="Name"
          name="name"
          required
        >
          <UInput
            v-model="state.name"
            class="w-full"
            placeholder="Alpha Company"
            autocomplete="organization"
            @update:model-value="serverErrors = []"
          />
        </UFormField>

        <UFormField
          label="Address"
          name="slug"
          :description="`The address is /t/${state.slug || 'alpha-company'}`"
          required
        >
          <UInput
            v-model="state.slug"
            class="w-full"
            placeholder="alpha-company"
            @update:model-value="slugEdited = true; serverErrors = []"
          />
        </UFormField>

        <div class="grid gap-4 sm:grid-cols-2">
          <UFormField
            label="Time zone"
            name="timeZone"
            description="Bookings are made and shown in this time zone."
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
              placeholder="65"
            >
              <template #leading>
                <span class="text-muted">+</span>
              </template>
            </UInput>
          </UFormField>
        </div>

        <UButton
          type="submit"
          label="Make organization"
          icon="i-lucide-plus"
          :loading="creating"
        />
      </UForm>
    </UPageCard>

    <UPageCard
      title="Join with a link"
      description="Paste the link someone sent you. An admin may have to let you in."
      variant="subtle"
    >
      <form
        class="flex gap-2"
        @submit.prevent="join"
      >
        <UInput
          v-model="pasted"
          class="flex-1"
          placeholder="https://.../join/..."
          aria-label="Link to join with"
        />
        <UButton
          type="submit"
          label="Join"
          color="neutral"
          :disabled="!pastedToken"
        />
      </form>
    </UPageCard>
  </div>
</template>
