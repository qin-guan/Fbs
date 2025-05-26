<script setup lang="ts">
import { useToast } from 'primevue/usetoast'
import type { FormSubmitEvent } from '@primevue/forms/form'
import type { FastEndpointsProblemDetails } from '~/api/models'

definePageMeta({
  layout: 'landing',
})

const router = useRouter()
const otp = ref('')

const toast = useToast()
const phone = useRoute().query.phone as string
const { mutate: mutateVerify, isPending: isPendingVerify } = useVerifyMutation()

function resolver() {
  const errors: Record<string, unknown> = {}

  if (otp.value.length === 0) {
    errors.otp = [{ message: 'OTP must be 6 digits.' }]
  }

  return {
    errors,
  }
}

async function onFormSubmit({ valid }: FormSubmitEvent) {
  if (!valid) {
    toast.add({
      severity: 'error',
      summary: 'Form is invalid.',
      life: 3000,
    })
    return
  }

  mutateVerify({ code: otp.value, phone }, {
    onError(error) {
      const e = error as FastEndpointsProblemDetails
      for (const error of e.errors ?? []) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: error.reason,
          life: 3000,
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
  <div class="flex-2">
    <div class="container mx-auto px-4 mt-6 max-w-md lg:mt-20">
      <Form
        v-slot="$form"
        :resolver="resolver"
        :initial-values="{ otp: '' }"
        class="flex flex-col gap-4"
        @submit="onFormSubmit"
      >
        <div class="flex flex-col gap-1">
          <div class="my-6 flex flex-col items-start gap-3">
            <h1 class="text-3xl font-semibold">
              Verify your OTP
            </h1>
            <span>
              Enter the OTP sent to your Telegram account
            </span>
            <Chip :label="phone" />
          </div>

          <Fluid>
            <InputOtp
              id="otp"
              v-model="otp"
              name="otp"
              :length="6"
              integer-only
              :disabled="isPendingVerify"
            />

            <Message
              v-if="$form.otp?.invalid"
              severity="error"
              size="small"
              variant="simple"
            >
              {{ $form.otp.error?.message }}
            </Message>

            <div class="mt-10 space-y-3">
              <Button
                type="submit"
                label="Login"
              />

              <Button
                v-slot="slotProps"
                as-child
                severity="secondary"
              >
                <NuxtLink
                  to="tg://resolve?domain=temasek_facility_booking_bot"
                  :class="slotProps.class"
                >
                  Open Telegram
                </NuxtLink>
              </Button>
            </div>
          </Fluid>
        </div>
      </Form>
    </div>
  </div>
</template>
