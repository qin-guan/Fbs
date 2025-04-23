<script setup lang="ts">
import * as v from 'valibot'
import { FetchError } from 'ofetch'

const toast = useToast()
const { public: { api } } = useRuntimeConfig()

const state = reactive({
  phone: '',
  otp: [],
  verify: false,
  pending: false,
})

const schema = v.object({
  phone: v.pipe(v.string(), v.length(8)),
})

async function login() {
  state.pending = true

  if (!state.verify) {
    try {
      await $fetch(`${api}/Auth/Login`, {
        method: 'POST',
        body: {
          phone: '65' + state.phone,
        },
      })
      state.verify = true
    }
    catch (e) {
      if (e instanceof FetchError) {
        const errors = e.data.errors

        for (const error of errors) {
          switch (error.code) {
            case 'EX01': {
              toast.add({
                color: 'error',
                title: 'Error',
                description: 'This number is not allow-listed. Please contact your unit S3 to add your number.',
              })
              break
            }

            case 'EX02': {
              toast.add({
                color: 'primary',
                title: 'Verify your number',
                description: 'This number is not verified. Please verify your number first.',
                actions: [
                  {
                    label: 'Verify',
                    color: 'primary',
                    onClick: () => {
                      window.open('https://t.me/temasek_facility_booking_bot')
                    },
                  },
                ],
              })
              break
            }

            default: {
              toast.add({
                color: 'error',
                title: 'Error',
                description: error.message,
              })
              break
            }
          }
        }
      }
    }
  }

  else {
    await $fetch(`${api}/Auth/Verify`, {
      method: 'POST',
      body: {
        phone: '65' + state.phone,
        code: state.otp.join(''),
      },
      credentials: 'include',
    })

    navigateTo('/booking')
  }

  state.pending = false
}
</script>

<template>
  <div class="flex-1 flex items-center justify-center">
    <UForm
      :schema="schema"
      :state="state"
      class="space-y-4"
      @submit="login"
    >
      <UFormField
        label="Phone"
        name="phone"
      >
        <UInput
          v-model="state.phone"
          type="phone"
        />
      </UFormField>

      <UFormField
        v-if="state.verify"
        label="OTP"
        name="otp"
      >
        <UPinInput
          v-model="state.otp"
          length="6"
          otp
          type="number"
        />
      </UFormField>

      <UButton type="submit">
        Login
      </UButton>
    </UForm>
  </div>
</template>
