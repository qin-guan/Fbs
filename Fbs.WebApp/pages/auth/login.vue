<script setup lang="ts">
import type { FormError, FormSubmitEvent } from '@nuxt/ui'
import type { FastEndpointsProblemDetails } from '~/api/models'

definePageMeta({
  layout: 'landing',
})

const { data: me } = useMe()
const showSignUpOnTelegramButton = ref(false)

const router = useRouter()
const toast = useToast()
const { mutate: mutateLogin, isPending: isPendingLogin } = useLoginMutation()

const state = reactive({
  phone: '',
})

// Mask the input as 9999-9999
watch(() => state.phone, (value) => {
  const digits = value.replace(/\D/g, '').slice(0, 8)
  const masked = digits.length > 4 ? `${digits.slice(0, 4)}-${digits.slice(4)}` : digits
  if (masked !== value) {
    state.phone = masked
  }
})

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []
  const digits = values.phone.replace(/\D/g, '')

  if (!digits) {
    errors.push({ name: 'phone', message: 'Phone number is required.' })
  }
  else if (digits.length !== 8) {
    errors.push({ name: 'phone', message: 'Phone number must be 8 digits.' })
  }

  return errors
}

function onFormError() {
  toast.add({
    title: 'Form is invalid.',
    color: 'error',
    icon: 'i-lucide-circle-x',
    duration: 3000,
  })
}

function onFormSubmit({ data }: FormSubmitEvent<typeof state>) {
  const phone = `65${data.phone.replace('-', '')}`

  mutateLogin({
    phone,
  }, {
    onError(error) {
      const e = error as FastEndpointsProblemDetails
      for (const error of e.errors ?? []) {
        if (error.code === 'EX02') {
          showSignUpOnTelegramButton.value = true
        }

        toast.add({
          title: 'Error',
          description: error.reason ?? undefined,
          color: 'error',
          icon: 'i-lucide-circle-x',
          duration: 3000,
        })
      }

      // The API answers with a bare 401 when an OTP was requested less than a minute ago
      if (!e.errors?.length) {
        toast.add({
          title: 'Error',
          description: e.responseStatusCode === 401
            ? 'Please wait a minute before requesting another OTP.'
            : 'Something went wrong. Please try again.',
          color: 'error',
          icon: 'i-lucide-circle-x',
          duration: 3000,
        })
      }
    },
    async onSuccess() {
      await router.push({
        path: '/auth/verify',
        query: { phone },
      })
    },
  })
}
</script>

<template>
  <div class="flex flex-1 justify-center px-4 py-10 lg:py-20">
    <div class="w-full max-w-sm">
      <UPageCard variant="subtle">
        <div class="space-y-1.5">
          <h1 class="text-2xl font-semibold text-highlighted">
            Login
          </h1>
          <p class="text-sm text-muted">
            Use your registered phone number to login.
          </p>
        </div>

        <UForm
          :state="state"
          :validate="validate"
          :validate-on="['input']"
          class="flex flex-col gap-4"
          @submit="onFormSubmit"
          @error="onFormError"
        >
          <UFormField
            label="Phone number"
            name="phone"
          >
            <UInput
              id="phone"
              v-model="state.phone"
              type="tel"
              inputmode="numeric"
              autocomplete="tel-national"
              placeholder="Example: 8888-9999"
              size="xl"
              class="w-full"
              :disabled="isPendingLogin"
              :ui="{ base: 'ps-13', leading: 'pointer-events-none' }"
            >
              <template #leading>
                <span class="text-sm text-muted">+65</span>
              </template>
            </UInput>
          </UFormField>

          <UButton
            v-if="showSignUpOnTelegramButton"
            to="https://t.me/temasek_facility_booking_bot"
            target="_blank"
            icon="i-simple-icons-telegram"
            label="Sign up on Telegram"
            color="neutral"
            variant="outline"
            size="lg"
            block
          />

          <UButton
            :loading="isPendingLogin"
            type="submit"
            label="Login"
            size="lg"
            block
          />
        </UForm>
      </UPageCard>

      <p
        v-if="me?.phone"
        class="mt-5 text-center text-sm text-muted"
      >
        You are already logged in as
        <NuxtLink
          to="/booking"
          class="font-semibold text-highlighted hover:underline"
        >
          {{ me?.phone }}
        </NuxtLink>
      </p>
    </div>
  </div>
</template>
