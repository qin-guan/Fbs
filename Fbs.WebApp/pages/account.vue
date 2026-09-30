<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query'
import { getMeExport, getMeTelegramQueryKey, useDeleteMeTelegram, useGetMe, useGetMeTelegram, usePostMeTelegramLink } from '~/api'

definePageMeta({
  layout: 'account',
})

useHead({ title: 'Your account' })

const toast = useToast()
const queryClient = useQueryClient()
const { data: me } = useGetMe()

const connecting = ref(false)
const { data: telegram } = useGetMeTelegram({ query: { refetchInterval: computed(() => connecting.value ? 3000 : false) } })
watch(() => telegram.value?.linked, (linked) => {
  if (linked && connecting.value) {
    connecting.value = false
    toast.add({ title: 'Telegram is connected', color: 'success' })
  }
})

const { mutateAsync: makeLink, isPending: makingLink } = usePostMeTelegramLink()
async function connect() {
  try {
    const link = await makeLink(undefined)
    connecting.value = true
    window.open(link.url, '_blank', 'noopener')
  }
  catch (error) {
    toast.add({ title: getErrorStatus(error) === 429 ? 'Too many tries' : 'Couldn\'t make the link', description: 'Wait a little, then try again.', color: 'error' })
  }
}

const { mutateAsync: disconnectTelegram, isPending: disconnecting } = useDeleteMeTelegram()
async function disconnect() {
  await disconnectTelegram(undefined)
  connecting.value = false
  await queryClient.invalidateQueries({ queryKey: getMeTelegramQueryKey() })
}

// A copy of what is kept about them, which is theirs to have
const downloading = ref(false)
async function downloadMyData() {
  downloading.value = true
  try {
    downloadJson('my-data.json', await getMeExport().unwrap())
  }
  catch (error) {
    toast.add({
      title: getErrorStatus(error) === 429 ? 'Too many tries' : 'Couldn\'t get your data',
      description: getErrorStatus(error) === 429 ? 'Wait a little, then try again.' : 'Try again in a moment.',
      color: 'error',
    })
  }
  finally {
    downloading.value = false
  }
}

const fields = computed(() => [
  { label: 'Name', description: 'What your account is called', value: me.value?.name },
  { label: 'Email', description: 'The email you sign in with', value: me.value?.email },
])
</script>

<template>
  <div class="space-y-6">
    <UPageCard
      title="Your account"
      description="You sign in with this, and it is in every organization you belong to."
      variant="naked"
    />

    <UPageCard variant="subtle">
      <template
        v-for="(field, index) in fields"
        :key="field.label"
      >
        <USeparator v-if="index > 0" />

        <UFormField
          :label="field.label"
          :description="field.description"
          class="flex max-sm:flex-col justify-between sm:items-center gap-4"
        >
          <UInput
            :model-value="field.value ?? ''"
            :aria-label="field.label"
            class="w-full sm:w-64"
            disabled
          />
        </UFormField>
      </template>
    </UPageCard>

    <UPageCard
      title="Your data"
      description="A copy of what is kept about you, as a file: your account, the organizations you are in, and what you booked."
      variant="subtle"
      icon="i-lucide-download"
    >
      <UButton
        label="Download my data"
        icon="i-lucide-download"
        color="neutral"
        variant="subtle"
        :loading="downloading"
        @click="downloadMyData"
      />
    </UPageCard>

    <UPageCard
      title="Telegram"
      description="Be told about bookings on Telegram, in every organization you belong to. It is only for notifications: it isn't how you sign in."
      variant="subtle"
      icon="i-simple-icons-telegram"
    >
      <div
        v-if="telegram?.linked"
        class="flex flex-wrap items-center gap-3"
      >
        <UBadge
          label="Connected"
          color="success"
          variant="subtle"
        />
        <UButton
          label="Connect another chat"
          color="neutral"
          variant="subtle"
          :loading="makingLink"
          @click="connect"
        />
        <UButton
          label="Disconnect"
          color="error"
          variant="ghost"
          :loading="disconnecting"
          @click="disconnect"
        />
      </div>

      <div
        v-else
        class="space-y-2"
      >
        <UButton
          label="Connect Telegram"
          icon="i-simple-icons-telegram"
          :loading="makingLink"
          @click="connect"
        />
        <p
          v-if="connecting"
          class="text-sm text-muted"
        >
          Press Start in Telegram. This page notices by itself.
        </p>
      </div>
    </UPageCard>
  </div>
</template>
