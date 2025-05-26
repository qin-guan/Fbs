<script setup lang="ts">
import { useToast } from 'primevue/usetoast'
import type { FormResolverOptions, FormSubmitEvent } from '@primevue/forms/form'
import type { FastEndpointsProblemDetails } from '~/api/models'

definePageMeta({
  layout: 'landing',
})

const { data: me } = useMe()

const router = useRouter()
const toast = useToast()
const { mutate: mutateLogin, isPending: isPendingLogin } = useLoginMutation()

function resolver({ values }: FormResolverOptions) {
  const errors: Record<string, unknown> = {}

  if (!values.phone) {
    errors.phone = [{ message: 'Phone number is required.' }]
  }

  return {
    values,
    errors,
  }
}

async function onFormSubmit({ valid, values }: FormSubmitEvent) {
  if (!valid) {
    toast.add({
      severity: 'error',
      summary: 'Form is invalid.',
      life: 3000,
    })
    return
  }

  mutateLogin({
    phone: `65${values.phone.replace('-', '')}`,
  }, {
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
      await router.push({
        path: '/auth/verify',
        query: { phone: `65${values.phone.replace('-', '')}` },
      })
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
        class="flex flex-col gap-4"
        @submit="onFormSubmit"
      >
        <div class="flex flex-col gap-1">
          <div class="my-6 space-y-3">
            <h1 class="text-3xl font-semibold">
              Login
            </h1>
            <span>
              Use your registered phone number to login.
            </span>
          </div>

          <InputMask
            id="phone"
            mask="9999-9999"
            name="phone"
            type="tel"
            placeholder="Example: 8888-9999"
            fluid
            :disabled="isPendingLogin"
          />

          <Message
            v-if="$form.phone?.invalid"
            severity="error"
            size="small"
            variant="simple"
          >
            {{ $form.phone.error?.message }}
          </Message>
        </div>

        <Button
          :loading="isPendingLogin"
          type="submit"
          label="Login"
        />
      </Form>
    </div>

    <div v-if="me?.phone">
      <div class="text-center mt-5">
        <p class="text-sm text-gray-501">
          You are already logged in as
          <span class="font-semibold text-gray-901">
            {{ me?.phone }}
          </span>
        </p>
      </div>
    </div>
  </div>
</template>
