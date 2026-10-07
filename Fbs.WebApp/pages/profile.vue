<script setup lang="ts">
import { useGetAuthMe } from '~/api'

definePageMeta({
  layout: 'app',
})

const { data: me } = useGetAuthMe()

const fields = computed(() => [
  { label: 'Rank / Name', description: 'The name the booking bot has', value: me.value?.name },
  { label: 'Phone', description: 'Shown with the country code', value: me.value?.phone },
  { label: 'Telegram ID', description: 'The chat id Telegram uses internally', value: me.value?.telegramChatId },
  { label: 'Notification group', description: 'Where your notices are sent', value: me.value?.notificationGroup },
])
</script>

<template>
  <UDashboardPanel id="profile">
    <template #header>
      <AppNavbar title="Your profile" />
    </template>

    <template #body>
      <div class="w-full lg:max-w-2xl mx-auto">
        <UPageCard
          title="Profile"
          description="What the booking bot has stored for you."
          variant="naked"
          class="mb-4"
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
      </div>
    </template>
  </UDashboardPanel>
</template>
