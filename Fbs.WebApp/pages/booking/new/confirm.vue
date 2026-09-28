<script setup lang="ts">
import type { FormError, FormErrorEvent, FormSubmitEvent } from '@nuxt/ui'
import { useQuery } from '@tanstack/vue-query'
import type { FastEndpointsProblemDetails } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const { $driver } = useNuxtApp()
const onboarded = useLocalStorage<boolean>('new-confirm-onboarded', false)

const router = useRouter()
const { df, tf } = useFormatter()
const { mutate: createMutate, isPending: createIsPending } = useCreateBookingMutation()

const route = useRoute()
const toast = useToast()

onMounted(() => {
  if (!onboarded.value) {
    $driver.setConfig({
      showProgress: true,
      steps: [
        { element: '#poc-rank-and-name', popover: { title: 'Autocomplete', description: 'Choose from the list of existing POCs, or enter your own!' } },
        {
          element: '#crumbs', popover: {
            title: 'Going back', description: 'Click to go back to the previous page!', onNextClick() {
              onboarded.value = true
              $driver.moveNext()
            },
          },
        },
      ],
      onCloseClick() {
        onboarded.value = true
      },
    })
    $driver.drive()
  }
})

const { data: prefilledData, error: prefilledDataError } = useQuery({
  queryKey: ['bookings', 'new', route.query],
  retry: false,
  queryFn: () => {
    if (!route.query['start-date'] || !route.query['end-date'] || !route.query['facility-name']) {
      throw new Error('Missing required query parameters')
    }

    return {
      startDateTime: new Date(route.query['start-date'] as string),
      endDateTime: new Date(route.query['end-date'] as string),
      facilityName: route.query['facility-name'] as string,
      originalQuery: route.query['original-query'] as string,
    }
  },
})

const state = reactive({
  conduct: '',
  pocName: '',
  pocPhone: '',
  description: '',
})

function optionSelect({ phone }: { phone: string }) {
  state.pocPhone = phone.slice(2)
}

function validate(values: typeof state): FormError[] {
  const errors: FormError[] = []

  if (!values.conduct) {
    errors.push({ name: 'conduct', message: 'Conduct name is required.' })
  }

  if (!values.pocName) {
    errors.push({ name: 'pocName', message: 'POC Rank and Name is required.' })
  }

  if (!values.pocPhone) {
    errors.push({ name: 'pocPhone', message: 'POC Phone is required.' })
  }
  else if (values.pocPhone.length !== 8) {
    errors.push({ name: 'pocPhone', message: 'POC Phone is not valid.' })
  }

  return errors
}

function onFormError(event: FormErrorEvent) {
  const id = event.errors[0]?.id
  if (id) {
    // Inputs are disabled while the form submits; focus once they are re-enabled
    setTimeout(() => document.getElementById(id)?.focus())
  }
}

function onFormSubmit({ data }: FormSubmitEvent<typeof state>) {
  if (!prefilledData.value) return

  const facilityName = prefilledData.value.facilityName

  createMutate({
    conduct: data.conduct,
    pocName: data.pocName,
    pocPhone: '65' + data.pocPhone,
    description: data.description,
    startDateTime: prefilledData.value.startDateTime,
    endDateTime: prefilledData.value.endDateTime,
    facilityName,
  }, {
    onError(error) {
      const e = error as FastEndpointsProblemDetails
      toast.add({
        title: 'Error creating booking',
        description: e.errors?.find(a => a)?.reason ?? undefined,
        color: 'error',
        icon: 'i-lucide-circle-x',
      })
    },
    async onSuccess(data) {
      toast.add({
        title: 'Booking created successfully',
        description: `Booking for ${facilityName} has been created.`,
        color: 'success',
        icon: 'i-lucide-circle-check',
      })
      if (data?.id) {
        await router.push(`/booking/${data?.id}`)
      }
      else {
        await router.push(`/booking`)
      }
    },
  })
}
</script>

<template>
  <UDashboardPanel id="booking-confirm">
    <template #header>
      <AppNavbar>
        <template #title>
          <UBreadcrumb
            :items="[
              { label: 'Bookings', to: '/booking' },
              { label: 'New', to: `/booking/new${prefilledData?.originalQuery ?? ''}`, slot: 'crumbs' },
              { label: prefilledData?.facilityName },
            ]"
          >
            <template #crumbs-label="{ item }">
              <span id="crumbs">{{ item.label }}</span>
            </template>
          </UBreadcrumb>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="w-full lg:max-w-3xl mx-auto">
        <UAlert
          v-if="prefilledDataError"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
          title="No time slot selected"
          description="Pick a facility and time slot on the timeline first."
          :actions="[{ label: 'Back to new booking', to: '/booking/new', color: 'error', variant: 'outline' }]"
        />

        <UForm
          v-if="prefilledData"
          :state="state"
          :validate="validate"
          :validate-on="['input']"
          class="flex flex-col gap-6"
          @submit="onFormSubmit"
          @error="onFormError"
        >
          <div class="flex flex-col gap-3">
            <h2 class="text-lg font-semibold text-highlighted">
              Confirm your booking
            </h2>

            <div class="flex flex-wrap items-center gap-2 text-sm text-muted">
              <UBadge
                :label="prefilledData.facilityName"
                icon="i-lucide-map-pin"
                color="neutral"
                variant="subtle"
              />
              <span class="flex items-center gap-1.5">
                <UIcon
                  name="i-lucide-clock"
                  class="size-4"
                />
                {{ df.format(prefilledData.startDateTime) }}, {{ tf.format(prefilledData.startDateTime) }} – {{ tf.format(prefilledData.endDateTime) }}
              </span>
            </div>
          </div>

          <UPageCard variant="subtle">
            <div class="grid gap-5 sm:grid-cols-2">
              <UFormField
                label="Conduct"
                name="conduct"
                class="sm:col-span-2"
              >
                <UInput
                  v-model="state.conduct"
                  placeholder="Conduct name"
                  size="xl"
                  autofocus
                  class="w-full"
                />
              </UFormField>

              <UFormField
                id="poc-rank-and-name"
                label="POC Rank and Name"
                name="pocName"
              >
                <PocNameInput
                  v-model="state.pocName"
                  placeholder="Search the nominal roll"
                  @select="optionSelect"
                />
              </UFormField>

              <UFormField
                label="POC Phone"
                name="pocPhone"
              >
                <UInput
                  v-model="state.pocPhone"
                  type="tel"
                  inputmode="numeric"
                  placeholder="8888 9999"
                  class="w-full"
                  :ui="{ base: 'ps-12', leading: 'pointer-events-none' }"
                >
                  <template #leading>
                    <span class="text-sm text-muted">+65</span>
                  </template>
                </UInput>
              </UFormField>

              <UFormField
                label="Description"
                name="description"
                class="sm:col-span-2"
              >
                <UTextarea
                  v-model="state.description"
                  :rows="3"
                  autoresize
                  class="w-full"
                />
              </UFormField>

              <UFormField label="Start">
                <UInput
                  :model-value="`${df.format(prefilledData.startDateTime)}, ${tf.format(prefilledData.startDateTime)}`"
                  icon="i-lucide-calendar"
                  disabled
                  class="w-full"
                />
              </UFormField>

              <UFormField label="End">
                <UInput
                  :model-value="`${df.format(prefilledData.endDateTime)}, ${tf.format(prefilledData.endDateTime)}`"
                  icon="i-lucide-calendar"
                  disabled
                  class="w-full"
                />
              </UFormField>
            </div>
          </UPageCard>

          <UAlert
            color="primary"
            variant="subtle"
            icon="i-lucide-heart-handshake"
            title="Be gracious to others!"
          >
            <template #description>
              <p>Book only what you need, and leave the facility in a better condition than you found it!</p>
              <p>Thank you :3</p>
            </template>
          </UAlert>

          <UButton
            :loading="createIsPending"
            label="Confirm"
            type="submit"
            size="lg"
            block
          />
        </UForm>
      </div>
    </template>
  </UDashboardPanel>
</template>
