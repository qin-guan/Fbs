<script setup lang="ts">
import { useGetAuthMe } from '~/api'

definePageMeta({
  layout: 'landing',
})

const { data: me } = useGetAuthMe()

const links = computed(() => me.value?.phone
  ? [
      { label: 'Make a booking', to: '/booking/new', icon: 'i-lucide-calendar-plus' },
      { label: 'View bookings', to: '/booking', color: 'neutral' as const, variant: 'subtle' as const },
    ]
  : [
      { label: 'Login', to: '/auth/login', trailingIcon: 'i-lucide-arrow-right' },
      { label: 'Sign up on Telegram', to: 'https://t.me/temasek_facility_booking_bot', target: '_blank', icon: 'i-simple-icons-telegram', color: 'neutral' as const, variant: 'subtle' as const },
    ])

const features = [
  {
    title: 'See what\'s booked',
    description: 'Every facility\'s schedule on a day or week timeline, grouped by facility type.',
    icon: 'i-lucide-calendar-range',
  },
  {
    title: 'Drag to book',
    description: 'Click and drag on an open slot, adjust the time, then confirm your conduct details.',
    icon: 'i-lucide-mouse-pointer-click',
  },
  {
    title: 'Log in with Telegram',
    description: 'No passwords. A one-time code is sent to your registered Telegram account.',
    icon: 'i-simple-icons-telegram',
  },
]
</script>

<template>
  <div class="w-full">
    <UPageHero
      title="3SIB Facility Booking"
      description="Check facility availability and book it for your conduct, all from your phone."
      :links="links"
      :ui="{ container: 'py-16 sm:py-24 lg:py-28' }"
    >
      <template #top>
        <div class="absolute inset-0 -z-10 bg-[radial-gradient(60%_50%_at_50%_0%,var(--ui-color-primary-100),transparent)] dark:bg-[radial-gradient(60%_50%_at_50%_0%,color-mix(in_oklab,var(--ui-color-primary-900)_45%,transparent),transparent)]" />
      </template>

      <template #headline>
        <img
          src="/images/logo.png"
          alt="3SIB crest"
          width="96"
          height="79"
          class="mx-auto h-20 w-auto"
        >
      </template>
    </UPageHero>

    <UContainer class="pb-16 sm:pb-24">
      <UPageGrid>
        <UPageCard
          v-for="feature in features"
          :key="feature.title"
          v-bind="feature"
          variant="subtle"
        />
      </UPageGrid>
    </UContainer>
  </div>
</template>
