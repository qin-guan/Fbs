<script setup lang="ts">
definePageMeta({
  layout: 'app',
})

const { data: me, isPending: meIsPending } = useMe()
</script>

<template>
  <div class="h-full flex flex-col">
    <AppNavbar>
      <template #content>
        <h2>Your profile</h2>
      </template>
    </AppNavbar>

    <div class="w-full lg:w-xl px-3 mx-auto divide-y-1 divide-gray-300">
      <div class="py-3 flex justify-between items-center">
        <span>
          <span class="font-semibold">
            Rank / Name
          </span>
          <br>
          <span>
            Your rank and name
          </span>
        </span>
        <InputText
          size="small"
          :model-value="me?.name"
          disabled
        />
      </div>

      <div class="py-3 flex justify-between items-center">
        <span>
          <span class="font-semibold">
            Phone
          </span>
          <br>
          <span>
            Your phone number (intl.)
          </span>
        </span>
        <InputText
          size="small"
          :model-value="me?.phone"
          disabled
        />
      </div>

      <div class="py-3 flex justify-between items-center">
        <span>
          <span class="font-semibold">
            Telegram ID
          </span>
          <br>
          <span>
            Your internal Telegram ID
          </span>
        </span>
        <InputText
          size="small"
          :model-value="me?.telegramChatId"
          disabled
        />
      </div>

      <div class="py-3 flex justify-between items-center">
        <span>
          <span class="font-semibold">
            Notification group
          </span>
          <br>
          <span>
            Subscribed notification group
          </span>
        </span>
        <InputText
          size="small"
          :model-value="me?.notificationGroup"
          disabled
        />
      </div>
    </div>
  </div>
</template>
