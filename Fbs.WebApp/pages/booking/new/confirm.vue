<script setup lang="ts">
import type { FormResolverOptions } from '@primevue/forms/form'
import { useQuery } from '@tanstack/vue-query'
import type { FastEndpointsProblemDetails } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const { $driver } = useNuxtApp()
const onboarded = useLocalStorage<boolean>('new-confirm-onboarded', false)

const router = useRouter()
const { data: nominalRoll, isPending: nominalRollIsPending } = useNominalRollMapping()
const nominalRollMiniSearch = useNominalRollMiniSearch()
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

const filteredItemsPhone = ref<string[]>([])
const filteredItemsName = computed(() => {
  return filteredItemsPhone.value.map(i => nominalRoll.value[i])
})

function searchItems(event) {
  filteredItemsPhone.value = nominalRollMiniSearch.value?.search(event.query).map(e => e.id) ?? []
}

function optionSelect(form, { value }) {
  if (!nominalRoll.value) return
  form.pocPhone.value = Object.entries(nominalRoll.value).find(e => e[1] == value)?.[0].slice(2) ?? ''
}

function resolver({ values }: FormResolverOptions) {
  const errors: Record<string, unknown> = {}

  if (values.conduct?.length === 0) {
    errors.conduct = [{ message: 'Conduct name is required.' }]
  }

  if (values.pocName?.length === 0) {
    errors.pocName = [{ message: 'POC Rank and Name is required.' }]
  }

  if (values.pocPhone?.length === 0) {
    errors.pocPhone = [{ message: 'POC Phone is required.' }]
  }

  if (values.pocPhone?.length !== 8) {
    errors.pocPhone = [{ message: 'POC Phone is not valid.' }]
  }

  return { errors }
}

function onFormSubmit({ valid, states }) {
  if (!valid || !prefilledData.value) return

  createMutate({
    conduct: states.conduct.value,
    pocName: states.pocName.value,
    pocPhone: states.pocPhone.value,
    description: states.description.value,
    startDateTime: prefilledData.value.startDateTime,
    endDateTime: prefilledData.value.endDateTime,
    facilityName: prefilledData.value.facilityName,
  }, {
    onError(error) {
      const e = error as FastEndpointsProblemDetails
      toast.add({
        severity: 'error',
        summary: 'Error creating booking',
        detail: e.errors?.find(a => a)?.reason,
      })
    },
    async onSuccess(data) {
      toast.add({
        severity: 'success',
        summary: 'Booking created successfully',
        detail: `Booking for ${prefilledData.value.facilityName} has been created.`,
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
  <div class="h-full flex flex-col">
    <Toast />

    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <Breadcrumb
            :pt="{ root: { style: 'padding: 0;' } }"
            :model="[
              { label: 'Bookings', route: '/booking' },
              { label: 'New', route: `/booking/new${prefilledData?.originalQuery}`, id: 'crumbs' },
              { label: prefilledData?.facilityName },
            ]"
          >
            <template #item="{ item, props }">
              <NuxtLink
                v-if="item.route"
                v-slot="{ href, navigate }"
                :to="item.route"
                custom
              >
                <a
                  :id="item.id"
                  :href="href"
                  v-bind="props.action"
                  @click="navigate"
                >
                  {{ item.label }}
                </a>
              </NuxtLink>
              <a
                v-else
                :href="item.url"
                :target="item.target"
                v-bind="props.action"
              >
                {{ item.label }}
              </a>
            </template>
          </Breadcrumb>
        </div>
      </template>
    </AppNavbar>

    <div
      v-if="prefilledData"
      class="p-3 flex flex-col flex-1"
    >
      <h2 class="text-lg font-semibold">
        Confirm your booking
      </h2>

      <Form
        v-slot="$form"
        :resolver="resolver"
        class="flex flex-1 flex-col gap-3 mt-5"
        :initial-values="{
          conduct: '',
          pocName: '',
          pocPhone: '',
          description: '',
        }"
        @submit="onFormSubmit"
      >
        <div class="flex">
          <Message
            severity="secondary"
            size="small"
          >
            {{ prefilledData?.facilityName }}
          </Message>
        </div>

        <Inplace active>
          <template #display>
            <h4 class="text-xl w-full">
              {{ $form.conduct.value || 'Conduct name' }}
            </h4>
          </template>

          <template #content="{ closeCallback }">
            <span class="inline-flex items-center gap-2">
              <InputText
                :value="$form.conduct?.value ?? ''"
                name="conduct"
                placeholder="Conduct name"
                size="large"
                autofocus
              />
              <Button
                text
                size="large"
                severity="danger"
                @click="closeCallback"
              >
                <template #icon>
                  <Icon name="i-lucide-check" />
                </template>
              </Button>
            </span>
          </template>
        </Inplace>

        <Message
          v-if="$form.conduct?.invalid"
          severity="error"
          size="small"
          variant="simple"
        >
          {{ $form.conduct.error?.message }}
        </Message>

        <FloatLabel
          id="poc-rank-and-name"
          variant="on"
        >
          <AutoComplete
            name="pocName"
            :virtual-scroller-options="{ itemSize: 38 }"
            dropdown
            :suggestions="filteredItemsName"
            fluid
            @complete="searchItems"
            @option-select="optionSelect($form, $event)"
          />
          <label for="pocName">POC Rank and Name</label>
        </FloatLabel>

        <Message
          v-if="$form.pocName?.invalid"
          severity="error"
          size="small"
          variant="simple"
        >
          {{ $form.pocName.error?.message }}
        </Message>

        <FormField
          v-slot="$field"
          name="pocPhone"
        >
          <FloatLabel variant="on">
            <InputText
              v-model="$field.value"
              type="tel"
              fluid
            />
            <label for="pocPhone">POC Phone</label>
          </FloatLabel>

          <Message
            v-if="$field.invalid"
            severity="error"
            size="small"
            variant="simple"
          >
            {{ $field.error?.message }}
          </Message>
        </FormField>

        <FloatLabel variant="on">
          <Textarea
            name="description"
            fluid
          />
          <label for="description">Description</label>
        </FloatLabel>

        <FloatLabel variant="on">
          <DatePicker
            v-model="prefilledData.startDateTime"
            name="start"
            disabled
            fluid
            show-time
          />

          <label for="start">Start</label>
        </FloatLabel>

        <FloatLabel variant="on">
          <DatePicker
            v-model="prefilledData.endDateTime"
            name="end"
            disabled
            fluid
            show-time
          />

          <label for="end">End</label>
        </FloatLabel>

        <Message
          size="small"
          class="my-3"
        >
          <strong>
            Be gracious to others!
          </strong>
          <p>Book only what you need, and leave the facility in a better condition than you found it!</p>
          <p>Thank you :3</p>
        </Message>

        <Button
          :loading="createIsPending"
          label="Confirm"
          type="submit"
        />
      </Form>
    </div>
  </div>
</template>
