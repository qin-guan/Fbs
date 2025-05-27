<script setup lang="ts">
import { FilterMatchMode, FilterOperator } from '@primevue/core/api'

definePageMeta({
  layout: 'app',
})

const router = useRouter()
const { height } = useWindowSize()

const { Alt_n, slash } = useMagicKeys({
  passive: false,
  onEventFired(e) {
    if (e.key === '/') {
      e.preventDefault()
    }
  },
})

const searchVisible = ref(false)
const tableHeight = computed(() => `${height.value - 48}px`)

const { data: facilities, isPending: facilitiesIsPending } = useFacilities()
const { data: bookings, isPending: bookingsIsPending } = useBookings()

const defaultFilters = {
  global: { value: null, matchMode: FilterMatchMode.CONTAINS },
  id: { value: null, matchMode: FilterMatchMode.CONTAINS },
  conduct: { value: null, matchMode: FilterMatchMode.CONTAINS },
  facilityName: { value: null, matchMode: FilterMatchMode.IN },
  startDateTime: { operator: FilterOperator.AND, constraints: [{ value: null, matchMode: FilterMatchMode.DATE_IS }] },
  pocName: { value: null, matchMode: FilterMatchMode.CONTAINS },
  pocPhone: { value: null, matchMode: FilterMatchMode.CONTAINS },
}

const filters = ref(defaultFilters)
const keywordSearchInput = useTemplateRef('keywordSearchInput')

whenever(Alt_n, async () => {
  await router.push('/booking/new')
})

whenever(slash, async () => {
  keywordSearchInput.value?.$el.focus()
})

function clearFilters() {
  filters.value = defaultFilters
}
</script>

<template>
  <div class="h-full flex flex-col">
    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <div class="space-x-3 flex items-center">
            <h2>Bookings</h2>
            <Button
              type="button"
              label="Clear"
              size="small"
              variant="text"
              @click="clearFilters"
            >
              <template #icon>
                <Icon name="i-lucide-funnel-x" />
              </template>
            </Button>
          </div>
          <div class="flex flex-row-reverse md:flex-row items-center gap-3">
            <Button
              v-slot="slotProps"
              size="small"
              as-child
            >
              <NuxtLink
                to="/booking/new"
                :class="slotProps.class"
              >
                New
                <Badge>
                  alt-n
                </Badge>
              </NuxtLink>
            </Button>

            <Button
              class="md:hidden!"
              variant="text"
              severity="secondary"
              @click="searchVisible = true"
            >
              <template #icon>
                <Icon name="i-lucide-search" />
              </template>
            </Button>

            <IconField class="hidden md:flex">
              <InputIcon>
                <Icon name="i-lucide-search" />
              </InputIcon>
              <InputText
                ref="keywordSearchInput"
                v-model="filters['global'].value"
                size="small"
                placeholder="Keyword Search"
              />
              <InputIcon>
                <Icon name="i-lucide-square-slash" />
              </InputIcon>
            </IconField>
          </div>
        </div>
      </template>
    </AppNavbar>

    <Dialog
      v-model:visible="searchVisible"
      position="top"
      :modal="true"
      :draggable="false"
      :pt="{ content: { class: 'p-0!' } }"
    >
      <template #header>
        <IconField class="mr-5">
          <InputIcon>
            <Icon name="i-lucide-search" />
          </InputIcon>
          <InputText
            v-model="filters['global'].value"
            size="small"
            placeholder="Keyword Search"
          />
        </IconField>
      </template>
    </Dialog>

    <DataTable
      v-model:filters="filters"
      show-gridlines
      :value="bookings"
      data-key="id"
      filter-display="menu"
      scrollable
      :global-filter-fields="['id', 'conduct', 'facilityName', 'pocName', 'pocPhone']"
      resizable-columns
      column-resize-mode="expand"
      removable-sort
      sort-mode="multiple"
      :scroll-height="tableHeight"
      :virtual-scroller-options="{ itemSize: 50 }"
      :loading="bookingsIsPending"
    >
      <template #empty>
        No bookings found.
      </template>

      <Column
        field="id"
        header="ID"
        style="height: 50px;"
        body-class="truncate"
      >
        <template #body="slotProps">
          <Button
            v-slot="buttonSlotProps"
            link
            as-child
          >
            <NuxtLink
              style="margin: 0; padding: 0;"
              :class="buttonSlotProps.class"
              :to="`/booking/${slotProps.data.id}`"
            >
              {{ slotProps.data.id.substring(0, 8) }}
            </NuxtLink>
          </Button>
        </template>
      </Column>
      <Column
        field="facilityName"
        header="Facility"
        filter-field="facilityName"
        style="height: 50px;"
        :show-filter-match-modes="false"
        body-class="truncate"
      >
        <template #filter="{ filterModel }">
          <MultiSelect
            v-model="filterModel.value"
            filter
            :options="facilities"
            option-label="name"
            option-value="name"
            placeholder="Any"
          />
        </template>
      </Column>
      <Column
        field="conduct"
        header="Conduct"
        style="height: 50px;"
        body-class="truncate"
      />
      <Column
        header="Duration"
        style="height: 50px;"
        body-class="truncate"
      >
        <template #body="slotProps">
          <span>{{ (slotProps.data.endDateTime - slotProps.data.startDateTime) / (1000 * 60 * 60) }} hours</span>
        </template>
      </Column>
      <Column
        field="startDateTime"
        filter-field="startDateTime"
        data-type="date"
        sortable
        header="Start"
        style="height: 50px;"
        body-class="truncate"
      >
        <template #filter="{ filterModel }">
          <DatePicker
            v-model="filterModel.value"
            date-format="mm/dd/yy"
            placeholder="mm/dd/yyyy"
          />
        </template>
        <template #body="slotProps">
          <span>{{ slotProps.data.startDateTime.toLocaleString() }}</span>
        </template>
      </Column>
      <Column
        header="POC"
        style="height: 50px;"
        body-class="truncate"
      >
        <template #body="slotProps">
          <span>{{ slotProps.data.pocName }} ({{ slotProps.data.pocPhone }})</span>
        </template>
      </Column>
    </DataTable>
  </div>
</template>
