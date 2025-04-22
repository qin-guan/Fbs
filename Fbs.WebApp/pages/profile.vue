<script setup lang="ts">
import { useQuery } from '@urql/vue'

definePageMeta({
  layout: 'app',
})

const me = useQuery({
  query: queries.meWithEverything,
  variables: {},
})
</script>

<template>
  <UDashboardPanel>
    <template #header>
      <UDashboardNavbar title="Profile">
        <template #toggle>
          <UDashboardSidebarToggle />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <section class="space-y-4">
        <div>
          <span class="font-semibold">ID</span>
          <br>
          <code class="text-lg">{{ me.data.value?.me?.id }}</code>
        </div>

        <div>
          <span class="font-semibold">Row</span>
          <br>
          <code class="text-lg">{{ me.data.value?.me?.row }}</code>
        </div>

        <div>
          <span class="font-semibold">Unit</span>
          <br>
          <span class="text-lg">{{ me.data.value?.me?.unit }}</span>
        </div>

        <div>
          <span class="font-semibold">Name</span>
          <br>
          <span class="text-lg">{{ me.data.value?.me?.name }}</span>
        </div>

        <div>
          <span class="font-semibold">Phone</span>
          <br>
          <span class="text-lg">{{ me.data.value?.me?.phone }}</span>
        </div>

        <div>
          <span class="font-semibold">Telegram Chat ID</span>
          <br>
          <code class="text-lg">{{ me.data.value?.me?.telegramChatId }}</code>
        </div>
      </section>
    </template>
  </UDashboardPanel>
</template>
