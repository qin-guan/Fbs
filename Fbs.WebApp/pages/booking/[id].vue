<script setup lang="ts">
import type {FbsWebApiDtosBookingWithUser} from '~/api/models'

definePageMeta({
  layout: 'app',
})

const {data: me} = useMe()
const toast = useToast()
const {data: booking} = useBooking(useRoute().params.id as string)

const {data: nominalRoll, isPending: nominalRollIsPending} = useNominalRollMapping()
const nominalRollMiniSearch = useNominalRollMiniSearch()
const {mutate: deleteMutate, isPending: deleteIsPending} = useDeleteBookingMutation(useRoute().params.id as string)
const {mutate: updateMutate, isPending: updateIsPending} = useUpdateBookingMutation(useRoute().params.id as string)

const updateValues = ref<FbsWebApiDtosBookingWithUser>({})
const deleteConfirmationDialog = ref(false)
const menu = useTemplateRef('menu')

const saveDisabled = computed(() => {
  if (!booking.value || !me.value) return true
  return booking.value.user?.phone !== me.value.phone
})

const filteredItemsPhone = ref<string[]>([])
const filteredItemsName = computed(() => {
  return filteredItemsPhone.value.map(i => nominalRoll.value[i])
})

const menuItems = [
  {
    label: 'Delete',
    command() {
      deleteConfirmationDialog.value = true
    },
  },
]

const userLink = computed(() => {
  if (!booking.value?.user?.phone) return ''
  return `https://api.whatsapp.com/send?phone=${booking.value?.user?.phone}`
})

const pocLink = computed(() => {
  if (!booking.value?.pocPhone) return ''
  return `https://api.whatsapp.com/send?phone=${booking.value?.pocPhone}`
})

whenever(booking, (newBooking) => {
  updateValues.value = {...newBooking}
})

function toggle(event: Event) {
  menu.value?.toggle(event)
}

function optionSelect({value}) {
  if (!nominalRoll.value) return
  updateValues.value.pocPhone = Object.entries(nominalRoll.value).find(e => e[1] == value)?.[0].slice(2) ?? ''
}

function searchItems(event) {
  filteredItemsPhone.value = nominalRollMiniSearch.value?.search(event.query).map(e => e.id) ?? []
}

function deleteBooking() {
  deleteMutate({}, {
    async onSuccess() {
      deleteConfirmationDialog.value = false
      await navigateTo('/booking')
    },
  })
}

function updateBooking() {
  updateMutate({...updateValues.value, pocPhone: '65' + updateValues.value.pocPhone}, {
    async onSuccess() {
      toast.add({
        severity: 'success',
        summary: 'Booking updated successfully.',
        life: 3000,
      })
    },
  })
}
</script>

<template>
  <div class="h-full flex flex-col">
    <Dialog
        v-model:visible="deleteConfirmationDialog"
        modal
        header="Delete booking"
        :style="{ width: '25rem' }"
    >
      <div class="mb-8 gap-3 flex flex-col">
        <span class="text-surface-500 dark:text-surface-400">
          Are you sure you want to delete this booking?
        </span>

        <Message severity="error">
          This is permanent and most definitely will result in pain and suffering if unintended!
        </Message>
      </div>

      <div class="flex justify-end gap-2">
        <Button
            type="button"
            label="Cancel"
            severity="secondary"
            size="small"
            @click="deleteConfirmationDialog = false"
        />
        <Button
            type="button"
            size="small"
            severity="danger"
            label="Delete"
            :loading="deleteIsPending"
            @click="deleteBooking"
        />
      </div>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <Breadcrumb
              :pt="{ root: { style: 'padding: 0;' } }"
              :model="[
              { label: 'Bookings', route: '/booking' },
              { label: booking?.facilityName ?? 'Loading...', route: `/booking/${booking?.id}` },
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

          <Button
              type="button"
              aria-haspopup="true"
              aria-controls="menu"
              variant="text"
              @click="toggle"
          >
            <template #icon>
              <Icon name="i-lucide-ellipsis-vertical"/>
            </template>
          </Button>

          <Menu
              id="menu"
              ref="menu"
              :model="menuItems"
              :popup="true"
          />
        </div>
      </template>
    </AppNavbar>

    <div class="p-3 flex flex-col gap-4">
      <div class="flex items-center justify-between flex-wrap">
        <Message
            severity="secondary"
            size="small"
        >
          {{ booking?.facilityName }}
        </Message>

        <span class="opacity-70 text-sm">
          Created by
          <NuxtLink
              class="hover:underline"
              :to="userLink"
              target="_blank"
          >
            {{ booking?.user?.name }}
          </NuxtLink>
        </span>
      </div>

      <Inplace>
        <template #display>
          <h3 class="text-2xl font-semibold w-full">
            {{ booking?.conduct }}
          </h3>
        </template>

        <template #content="{ closeCallback }">
          <span class="inline-flex items-center gap-2">
            <InputText
                v-model="updateValues.conduct"
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
                <Icon name="i-lucide-check"/>
              </template>
            </Button>
          </span>
        </template>
      </Inplace>

      <FloatLabel
          variant="on"
          class="mt-3"
      >
        <AutoComplete
            v-model="updateValues.pocName"
            :virtual-scroller-options="{ itemSize: 38 }"
            dropdown
            :suggestions="filteredItemsName"
            fluid
            @complete="searchItems"
            @option-select="optionSelect"
        />
        <label for="pocName">POC Rank and Name</label>
      </FloatLabel>

      <FloatLabel variant="on">
        <InputText
            v-model="updateValues.pocPhone"
            type="tel"
            fluid
        />
        <label for="pocPhone">POC Phone</label>
      </FloatLabel>

      <FloatLabel variant="on">
        <Textarea
            id="description"
            v-model="updateValues.description"
            fluid
        />
        <label for="description">Description</label>
      </FloatLabel>

      <FloatLabel
          v-tooltip="'Create a new booking if you wish to change the date'"
          variant="on"
      >
        <DatePicker
            id="start"
            v-model="updateValues.startDateTime"
            disabled
            fluid
            show-time
        />

        <label for="start">Start</label>
      </FloatLabel>

      <FloatLabel
          v-tooltip="'Create a new booking if you wish to change the date'"
          variant="on"
      >
        <DatePicker
            id="end"
            v-model="updateValues.endDateTime"
            disabled
            fluid
            show-time
        />

        <label for="end">End</label>
      </FloatLabel>

      <Button
          v-slot="slotProps"
          outlined
          as-child
      >
        <NuxtLink
            :class="slotProps.class"
            :to="pocLink"
            target="_blank"
        >
          Contact POC
        </NuxtLink>
      </Button>

      <Button
          label="Save"
          type="submit"
          :loading="updateIsPending"
          :disabled="saveDisabled"
          @click="updateBooking"
      />
    </div>
  </div>
</template>
