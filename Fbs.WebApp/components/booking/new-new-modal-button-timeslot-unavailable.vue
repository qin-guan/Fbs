<script setup lang="ts">
import { useQuery } from '@urql/vue'

const { tf } = useFormatter()

defineSlots<{
  ['modal-header'](): unknown
  ['modal-body'](): unknown
}>()

const emit = defineEmits(['click', 'mouseover'])

const props = defineProps<{
  slot: {
    startDateTime?: string | null
    endDateTime?: string | null
    booking: {
      conduct?: string | null
      description?: string | null
      pocName?: string | null
      pocPhone?: string | null
      user?: {
        unit?: string | null
      } | null
    }
  }
  timeSlot: string
  tooltip: string
  facilityName: string
}>()

const user = useQuery({
  query: queries.meWithEverything,
  variables: {},
})
const open = ref(false)
const slotDate = computed(() => new Date(props.timeSlot))
const slotStartDateTime = computed(() => new Date(props.slot.startDateTime || ''))
const slotEndDateTime = computed(() => new Date(props.slot.endDateTime || ''))
const pocContactHref = computed(() => {
  const message = encodeURIComponent(`
Hello, I am ${user.data.value?.me?.name} from ${user.data.value?.me?.unit}.

I would like to check if you would be open to sharing your slot

at ${props.facilityName} for ${props.slot.booking.conduct} 
from ${tf.format(slotStartDateTime.value)} to ${tf.format(slotEndDateTime.value)}.

Let me know if you are open to it, thanks!
  `)

  return `https://api.whatsapp.com/send?phone=${encodeURIComponent(props.slot.booking.pocPhone ?? '')}&text=${message}`
})
</script>

<template>
  <UModal
    v-model:open="open"
    title="Unavailable"
  >
    <div class="relative">
      <div class="flex items-center justify-center bg-red-900 rounded-full absolute top-[-7px] right-[-5px] px-2">
        <span class="text-xs text-white">
          {{ props.tooltip }}
        </span>
      </div>
      <UCard
        :ui="{ body: `p-2 sm:p-2 text-center text-sm border-1 rounded-sm cursor-pointer bg-red-100 border-red-400` }"
        @click="emit('click')"
        @mouseover="emit('mouseover')"
      >
        {{ tf.format(slotDate) }}
      </UCard>
    </div>
    <template #body>
      <div>
        <div class="flex justify-between items-center">
          <span class="font-semibold">
            Duration
          </span>
          <span>
            {{ tf.formatRange(slotStartDateTime, slotEndDateTime) }}
          </span>
        </div>

        <div class="flex justify-between items-center">
          <span class="font-semibold">
            Unit
          </span>
          <span>
            {{ props.slot.booking.user?.unit || 'N/A' }}
          </span>
        </div>

        <div class="flex justify-between items-center">
          <span class="font-semibold">
            Conduct
          </span>
          <span>
            {{ props.slot.booking.conduct || 'N/A' }}
          </span>
        </div>

        <div class="flex justify-between items-center">
          <span class="font-semibold">
            Description
          </span>
          <span>
            {{ props.slot.booking.description || 'N/A' }}
          </span>
        </div>

        <div class="flex justify-between items-center">
          <span class="font-semibold">
            PoC
          </span>
          <span>
            {{ props.slot.booking.pocName || 'N/A' }} (<ULink :href="pocContactHref">{{ props.slot.booking.pocPhone }}
            </ULink>)
          </span>
        </div>
      </div>
    </template>
  </UModal>
</template>
