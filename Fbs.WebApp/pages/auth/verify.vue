<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { usePostAuthVerify } from '~/api'

definePageMeta({
  layout: 'landing',
})

const router = useRouter()
const state = reactive({
  otp: [] as number[],
})
const otp = computed(() => state.otp.join(''))

const toast = useToast()
const phone = useRoute().query.phone as string
const { mutate: mutateVerify, isPending: isPendingVerify } = usePostAuthVerify()

const displayPhone = computed(() => {
  const match = /^65(\d{4})(\d{4})$/.exec(phone ?? '')
  return match ? `+65 ${match[1]} ${match[2]}` : phone
})

function validate(): FormError[] {
  const errors: FormError[] = []

  if (otp.value.length !== 6) {
    errors.push({ name: 'otp', message: 'OTP must be 6 digits.' })
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

function onFormSubmit() {
  mutateVerify({ body: { code: otp.value, phone } }, {
    onError(error) {
      const e = getProblemDetails(error)
      for (const error of e?.errors ?? []) {
        toast.add({
          title: 'Error',
          description: error.reason ?? undefined,
          color: 'error',
          icon: 'i-lucide-circle-x',
          duration: 3000,
        })
      }

      // The API rejects a wrong or expired OTP with a bare 401
      if (!e?.errors?.length) {
        toast.add({
          title: 'Error',
          description: 'The OTP is invalid or has expired.',
          color: 'error',
          icon: 'i-lucide-circle-x',
          duration: 3000,
        })
      }
    },
    async onSuccess() {
      await router.push('/booking')
    },
  })
}
</script>

<template>
  <div class="flex flex-1 justify-center px-4 py-10 lg:py-20">
    <div class="w-full max-w-sm">
      <UPageCard variant="subtle">
        <div class="flex flex-col items-start gap-3">
          <div class="space-y-1.5">
            <h1 class="text-2xl font-semibold text-highlighted">
              Verify your OTP
            </h1>
            <p class="text-sm text-muted">
              Enter the OTP sent to your Telegram account
            </p>
          </div>

          <UBadge
            :label="displayPhone"
            icon="i-lucide-smartphone"
            color="neutral"
            variant="subtle"
            size="lg"
          />
        </div>

        <UForm
          :state="state"
          :validate="validate"
          :validate-on="['input']"
          class="flex flex-col gap-4"
          @submit="onFormSubmit"
          @error="onFormError"
        >
          <UFormField name="otp">
            <UPinInput
              id="otp"
              v-model="state.otp"
              :length="6"
              type="number"
              otp
              autofocus
              size="xl"
              class="w-full justify-between"
              :disabled="isPendingVerify"
            />
          </UFormField>

          <div class="mt-4 flex flex-col gap-3">
            <UButton
              type="submit"
              label="Login"
              size="lg"
              :loading="isPendingVerify"
              block
            />

            <UButton
              to="tg://resolve?domain=temasek_facility_booking_bot"
              external
              icon="i-simple-icons-telegram"
              label="Open Telegram"
              color="neutral"
              variant="subtle"
              size="lg"
              block
            />
          </div>
        </UForm>
      </UPageCard>
    </div>
  </div>
</template>
