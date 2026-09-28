<script setup lang="ts">
definePageMeta({
  layout: 'app',
})

const { data: me } = useMe()

const fields = computed(() => [
  { label: 'Rank / Name', description: 'Your rank and name', value: me.value?.name },
  { label: 'Phone', description: 'Your phone number (intl.)', value: me.value?.phone },
  { label: 'Telegram ID', description: 'Your internal Telegram ID', value: me.value?.telegramChatId },
  { label: 'Notification group', description: 'Subscribed notification group', value: me.value?.notificationGroup },
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
          description="Your account details, as registered with the booking bot."
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
