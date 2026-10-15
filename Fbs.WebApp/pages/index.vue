<script setup lang="ts">
import { site } from '~/lib/site'

definePageMeta({
  layout: 'marketing',
})

// This page is drawn when the app is built (see routeRules in nuxt.config.ts), so everything here is in its HTML for search engines
// and link previews. What depends on who is signed in is in useHomeLinks, and the way out for somebody signed in is only drawn in the browser.
const accounts = usesAccounts()
const { primary } = useHomeLinks()
const { siteUrl } = useRuntimeConfig().public

const spaces = [
  { label: 'Meeting rooms', icon: 'i-lucide-presentation' },
  { label: 'Classrooms', icon: 'i-lucide-school' },
  { label: 'Sports courts', icon: 'i-lucide-volleyball' },
  { label: 'Halls', icon: 'i-lucide-landmark' },
  { label: 'Labs', icon: 'i-lucide-flask-conical' },
  { label: 'Gyms', icon: 'i-lucide-dumbbell' },
  { label: 'Studios', icon: 'i-lucide-music' },
  { label: 'Training areas', icon: 'i-lucide-flag' },
  { label: 'Function rooms', icon: 'i-lucide-party-popper' },
]

const features = [
  {
    title: 'See what\'s free at a glance',
    description: 'A timeline of every facility, by the day or the week, grouped by type. Spot an open slot in seconds instead of asking around.',
    icon: 'i-lucide-calendar-range',
  },
  {
    title: 'Drag to book',
    description: 'Drag across an open stretch of the calendar, add the details, and it\'s booked. A built-in walkthrough shows new people how.',
    icon: 'i-lucide-mouse-pointer-click',
  },
  {
    title: 'No double bookings',
    description: 'Every booking is checked against the ones already made, so a facility is never booked twice for the same time.',
    icon: 'i-lucide-shield-check',
  },
  {
    title: 'Book several at once',
    description: 'Pick several facilities and days in one go. They\'re booked together, or not at all if any of them clashes.',
    icon: 'i-lucide-layers',
  },
  {
    title: 'Telegram notifications',
    description: 'Hear about new, changed and cancelled bookings on Telegram, for your own unit or the whole organization.',
    icon: 'i-simple-icons-telegram',
  },
  {
    title: 'Google Calendar sync',
    description: 'Copy every booking to your organization\'s Google Calendar, and keep it up to date as bookings change.',
    icon: 'i-lucide-calendar-sync',
  },
]

const steps = [
  {
    title: 'Create your organization',
    description: 'Give it a name and an address, pick its time zone, and you\'re its first admin.',
  },
  {
    title: 'Add facilities and people',
    description: 'List what can be booked, group people into units, and share an invite link to bring them in.',
  },
  {
    title: 'Start booking',
    description: 'Everyone sees what\'s free and books it from a phone, tablet or computer. Nothing to install.',
  },
]

const adminTools = [
  {
    title: 'Units and roles',
    description: 'Group people into units, and choose who can manage facilities, people and settings.',
    icon: 'i-lucide-users-round',
  },
  {
    title: 'Invite links with approval',
    description: 'People join with a link, straight away or once an admin lets them in.',
    icon: 'i-lucide-link',
  },
  {
    title: 'A full history',
    description: 'See who changed what, and when, in the organization\'s audit history.',
    icon: 'i-lucide-history',
  },
  {
    title: 'Your data, any time',
    description: 'Download a copy of everything kept about your organization whenever you need it.',
    icon: 'i-lucide-download',
  },
  {
    title: 'Your time zone',
    description: 'Bookings follow the organization\'s clock, wherever people book from.',
    icon: 'i-lucide-globe',
  },
]

const people = [
  { name: 'Alex Tan', unit: 'Operations', role: 'Admin' },
  { name: 'Priya Nair', unit: 'Science', role: 'Member' },
  { name: 'Marcus Lee', unit: 'Sports', role: 'Member' },
]

const useCases = [
  {
    title: 'Schools and universities',
    description: 'Classrooms, labs, lecture theatres and halls, shared fairly between departments, clubs and student groups.',
    icon: 'i-lucide-graduation-cap',
  },
  {
    title: 'Sports clubs and leisure',
    description: 'Courts, pitches, pools and gyms, booked by teams and members without the clashes.',
    icon: 'i-lucide-trophy',
  },
  {
    title: 'Offices and coworking',
    description: 'Meeting rooms, focus booths and event spaces for teams that share a floor or a building.',
    icon: 'i-lucide-building-2',
  },
  {
    title: 'Military and uniformed units',
    description: 'Training areas, briefing rooms and parade squares, shared between units and planned for every conduct.',
    icon: 'i-lucide-shield',
  },
  {
    title: 'Communities and residences',
    description: 'Function rooms, multipurpose halls and barbecue pits for residents and community groups.',
    icon: 'i-lucide-house',
  },
  {
    title: 'Churches and non-profits',
    description: 'Halls, rooms and studios shared between ministries, volunteers and programmes.',
    icon: 'i-lucide-heart-handshake',
  },
]

const faqs = [
  {
    label: 'What is Book A Space?',
    content: 'Book A Space is an online facility booking system for organizations that share spaces. Members see what\'s free and book rooms, courts, halls and other facilities in a few taps, and admins decide what can be booked and who can book it.',
  },
  {
    label: 'What kinds of facilities can I manage?',
    content: 'Anything that is booked by the hour: meeting rooms, classrooms, sports courts, halls, labs, studios, training areas and more. Facilities are grouped by type, and you choose the shortest booking you allow.',
  },
  {
    label: 'How does Book A Space prevent double bookings?',
    content: 'Every booking is checked against the bookings already made for that facility before it goes through. When several slots are booked together, they are made all at once or not at all, so nobody is left with half a plan.',
  },
  {
    label: 'How do people join my organization?',
    content: 'Share an invite link. You can let people in straight away or approve each of them first, and admins can also add people by their phone number.',
  },
  {
    label: 'Does it work on my phone?',
    content: 'Yes. Book A Space runs in the browser on any phone, tablet or computer, with nothing to install, and it was designed for booking from a phone first.',
  },
  {
    label: 'Does it handle time zones?',
    content: 'Yes. Every booking follows your organization\'s time zone, so a 9:00 slot means 9:00 where your facilities are, wherever people book from.',
  },
  {
    label: 'Can I get my data out?',
    content: 'Yes. Admins can download a copy of their organization\'s data at any time, and everybody can download what is kept about them.',
  },
]

// Shown by search engines as the page's title, without the name added after it (see app.vue), as it has it already
useSeoMeta({
  title: site.title,
  robots: 'index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1',
})

// What the page is about, for search engines: https://schema.org, and https://developers.google.com/search/docs/appearance/structured-data
const home = `${siteUrl}/`
useHead({
  script: [{
    type: 'application/ld+json',
    innerHTML: JSON.stringify({
      '@context': 'https://schema.org',
      '@graph': [
        {
          '@type': 'Organization',
          '@id': `${home}#organization`,
          'name': site.name,
          'url': home,
          'logo': { '@type': 'ImageObject', 'url': `${siteUrl}/icon-512.png`, 'width': 512, 'height': 512 },
        },
        {
          '@type': 'WebSite',
          '@id': `${home}#website`,
          'name': site.name,
          'url': home,
          'description': site.description,
          'inLanguage': 'en',
          'publisher': { '@id': `${home}#organization` },
        },
        {
          '@type': 'WebPage',
          '@id': `${home}#webpage`,
          'name': site.title,
          'url': home,
          'description': site.description,
          'inLanguage': 'en',
          'isPartOf': { '@id': `${home}#website` },
          'about': { '@id': `${home}#app` },
          'primaryImageOfPage': { '@type': 'ImageObject', 'url': `${siteUrl}/og-image.png`, 'width': 1200, 'height': 630 },
        },
        {
          '@type': 'WebApplication',
          '@id': `${home}#app`,
          'name': site.name,
          'url': home,
          'description': site.description,
          'applicationCategory': 'BusinessApplication',
          'applicationSubCategory': 'Facility booking',
          'operatingSystem': 'Any (web browser)',
          'browserRequirements': 'Requires JavaScript and a modern web browser',
          'image': `${siteUrl}/og-image.png`,
          'featureList': [...features, ...adminTools].map(feature => feature.title),
          'publisher': { '@id': `${home}#organization` },
        },
        {
          '@type': 'FAQPage',
          '@id': `${home}#faq`,
          'isPartOf': { '@id': `${home}#webpage` },
          'mainEntity': faqs.map(faq => ({
            '@type': 'Question',
            'name': faq.label,
            'acceptedAnswer': { '@type': 'Answer', 'text': faq.content },
          })),
        },
      ],
    }),
  }],
})
</script>

<template>
  <div>
    <!-- Somebody who is signed in is taken to where they belong -->
    <ClientOnly v-if="accounts">
      <HomeRedirect />
    </ClientOnly>

    <UPageHero
      :links="primary"
      :ui="{
        container: 'pt-16 pb-24 sm:pt-24 sm:pb-32 lg:pt-28',
        title: 'text-5xl sm:text-7xl text-balance tracking-tight font-bold text-highlighted',
        description: 'text-lg sm:text-xl/8 text-muted text-pretty max-w-2xl mx-auto',
      }"
    >
      <template #top>
        <div
          class="absolute inset-0 -z-10 overflow-hidden"
          aria-hidden="true"
        >
          <div class="absolute inset-0 bg-[radial-gradient(60%_50%_at_50%_0%,var(--ui-color-primary-100),transparent)] dark:bg-[radial-gradient(60%_50%_at_50%_0%,color-mix(in_oklab,var(--ui-color-primary-900)_45%,transparent),transparent)]" />
          <div class="absolute inset-0 opacity-60 [background-image:linear-gradient(to_right,var(--ui-border)_1px,transparent_1px),linear-gradient(to_bottom,var(--ui-border)_1px,transparent_1px)] [background-size:56px_56px] [mask-image:radial-gradient(ellipse_70%_55%_at_50%_0%,black,transparent)]" />
        </div>
      </template>

      <template #headline>
        <span class="inline-flex items-center gap-2 rounded-full bg-default/80 px-3 py-1 text-sm font-medium text-toned ring ring-default backdrop-blur">
          <UIcon
            name="i-lucide-sparkles"
            class="size-4 text-primary"
          />
          Facility booking, made simple
        </span>
      </template>

      <template #title>
        The easy way to book <span class="text-primary">shared spaces</span>
      </template>

      <template #description>
        Book A Space is the facility booking platform for organizations that share rooms, courts, halls and labs. See what's free, book it in a few taps, and never double book again.
      </template>

      <HomePreview class="mx-auto mt-4 w-full max-w-5xl" />
    </UPageHero>

    <section
      aria-labelledby="spaces-title"
      class="border-y border-default bg-elevated/30"
    >
      <UContainer class="py-10">
        <h2
          id="spaces-title"
          class="text-center text-sm font-medium text-muted"
        >
          One place to book every kind of shared space
        </h2>
        <ul class="mt-6 flex flex-wrap justify-center gap-2.5">
          <li
            v-for="space in spaces"
            :key="space.label"
            class="inline-flex items-center gap-2 rounded-full bg-default px-3.5 py-1.5 text-sm text-toned ring ring-default"
          >
            <UIcon
              :name="space.icon"
              class="size-4 text-primary"
            />
            {{ space.label }}
          </li>
        </ul>
      </UContainer>
    </section>

    <section
      id="features"
      aria-labelledby="features-title"
      class="scroll-mt-(--ui-header-height)"
    >
      <UContainer class="py-24 sm:py-32">
        <div class="mx-auto max-w-2xl text-center">
          <p class="font-semibold text-primary">
            Features
          </p>
          <h2
            id="features-title"
            class="mt-2 text-3xl font-bold tracking-tight text-pretty text-highlighted sm:text-4xl"
          >
            Everything you need to run shared spaces
          </h2>
          <p class="mt-4 text-lg text-muted text-pretty">
            Replace the spreadsheets, group chats and paper sign-up sheets with one booking system everybody can use.
          </p>
        </div>

        <ul class="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          <li
            v-for="feature in features"
            :key="feature.title"
            class="group relative rounded-2xl bg-default p-6 ring ring-default transition hover:ring-primary/40 hover:shadow-lg hover:shadow-primary/5"
          >
            <span class="flex size-11 items-center justify-center rounded-xl bg-primary/10 text-primary ring ring-primary/20 transition group-hover:bg-primary group-hover:text-inverted">
              <UIcon
                :name="feature.icon"
                class="size-5"
              />
            </span>
            <h3 class="mt-5 text-base font-semibold text-highlighted">
              {{ feature.title }}
            </h3>
            <p class="mt-2 text-sm/6 text-muted">
              {{ feature.description }}
            </p>
          </li>
        </ul>
      </UContainer>
    </section>

    <section
      id="how-it-works"
      aria-labelledby="how-it-works-title"
      class="scroll-mt-(--ui-header-height) border-y border-default bg-elevated/30"
    >
      <UContainer class="py-24 sm:py-32">
        <div class="mx-auto max-w-2xl text-center">
          <p class="font-semibold text-primary">
            How it works
          </p>
          <h2
            id="how-it-works-title"
            class="mt-2 text-3xl font-bold tracking-tight text-pretty text-highlighted sm:text-4xl"
          >
            Set up in three steps
          </h2>
          <p class="mt-4 text-lg text-muted text-pretty">
            Bring your facilities and your people into one place, and let everybody book what they need.
          </p>
        </div>

        <div class="relative mt-16">
          <div
            class="absolute top-11 right-[16%] left-[16%] hidden h-px bg-linear-to-r from-transparent via-primary/40 to-transparent lg:block"
            aria-hidden="true"
          />
          <ol class="relative grid gap-6 lg:grid-cols-3">
            <li
              v-for="(step, index) in steps"
              :key="step.title"
              class="relative rounded-2xl bg-default p-6 text-center ring ring-default"
            >
              <span class="mx-auto flex size-10 items-center justify-center rounded-full bg-primary text-sm font-bold text-inverted shadow-lg shadow-primary/30">
                {{ index + 1 }}
              </span>
              <h3 class="mt-5 text-base font-semibold text-highlighted">
                {{ step.title }}
              </h3>
              <p class="mt-2 text-sm/6 text-muted">
                {{ step.description }}
              </p>
            </li>
          </ol>
        </div>
      </UContainer>
    </section>

    <section aria-labelledby="admins-title">
      <UContainer class="grid items-center gap-16 py-24 sm:py-32 lg:grid-cols-2">
        <div>
          <p class="font-semibold text-primary">
            For admins
          </p>
          <h2
            id="admins-title"
            class="mt-2 text-3xl font-bold tracking-tight text-pretty text-highlighted sm:text-4xl"
          >
            Stay in control, without the admin work
          </h2>
          <p class="mt-4 text-lg text-muted text-pretty">
            Decide what can be booked and who can book it. Book A Space keeps the record, so you don't have to.
          </p>

          <ul class="mt-10 space-y-6">
            <li
              v-for="tool in adminTools"
              :key="tool.title"
              class="flex gap-4"
            >
              <span class="flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary ring ring-primary/20">
                <UIcon
                  :name="tool.icon"
                  class="size-4.5"
                />
              </span>
              <div>
                <h3 class="font-semibold text-highlighted">
                  {{ tool.title }}
                </h3>
                <p class="mt-1 text-sm/6 text-muted">
                  {{ tool.description }}
                </p>
              </div>
            </li>
          </ul>
        </div>

        <!-- A picture of the people page, drawn with HTML -->
        <div
          class="relative"
          role="img"
          aria-label="An organization's people in Book A Space: members in their units with their roles, an invite link, and a request to join waiting for approval"
        >
          <div
            class="pointer-events-none absolute -inset-12 -z-10 bg-[radial-gradient(closest-side,color-mix(in_oklab,var(--ui-primary)_18%,transparent),transparent)] dark:opacity-60"
            aria-hidden="true"
          />
          <div class="overflow-hidden rounded-2xl bg-default shadow-2xl shadow-primary/10 ring ring-default">
            <div class="flex items-center justify-between gap-3 border-b border-default px-5 py-4">
              <div>
                <p class="font-semibold text-highlighted">
                  People
                </p>
                <p class="text-xs text-muted">
                  Northfield Academy · 4 people
                </p>
              </div>
              <span class="inline-flex items-center gap-1.5 rounded-md bg-primary px-2.5 py-1 text-xs font-medium text-inverted">
                <UIcon
                  name="i-lucide-user-plus"
                  class="size-3.5"
                />
                Invite
              </span>
            </div>

            <div class="border-b border-default bg-elevated/40 px-5 py-3">
              <div class="flex items-center gap-2 rounded-lg bg-default px-3 py-2 text-xs ring ring-default">
                <UIcon
                  name="i-lucide-link"
                  class="size-3.5 shrink-0 text-primary"
                />
                <span class="min-w-0 flex-1 truncate text-muted">bookaspace.app/join/7Kq2…</span>
                <span class="inline-flex items-center gap-1 font-medium text-highlighted">
                  <UIcon
                    name="i-lucide-copy"
                    class="size-3.5"
                  />
                  Copy
                </span>
              </div>
            </div>

            <ul class="divide-y divide-default">
              <li
                v-for="person in people"
                :key="person.name"
                class="flex items-center gap-3 px-5 py-3.5"
              >
                <span class="flex size-9 shrink-0 items-center justify-center rounded-full bg-elevated text-xs font-semibold text-toned ring ring-default">
                  {{ person.name.split(' ').map(part => part[0]).join('') }}
                </span>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium text-highlighted">
                    {{ person.name }}
                  </p>
                  <p class="truncate text-xs text-muted">
                    {{ person.unit }}
                  </p>
                </div>
                <span
                  class="rounded-md px-2 py-0.5 text-xs font-medium ring ring-inset"
                  :class="person.role === 'Admin' ? 'bg-primary/10 text-primary ring-primary/25' : 'bg-elevated text-toned ring-default'"
                >
                  {{ person.role }}
                </span>
              </li>
              <li class="flex flex-wrap items-center gap-3 bg-warning/5 px-5 py-3.5">
                <span class="flex size-9 shrink-0 items-center justify-center rounded-full bg-warning/15 text-xs font-semibold text-warning">
                  JO
                </span>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium text-highlighted">
                    Jordan Ong
                  </p>
                  <p class="truncate text-xs text-muted">
                    Asked to join · Drama
                  </p>
                </div>
                <div class="flex gap-2 text-xs font-medium">
                  <span class="rounded-md px-2.5 py-1 text-muted ring ring-default">Decline</span>
                  <span class="rounded-md bg-primary px-2.5 py-1 text-inverted">Let in</span>
                </div>
              </li>
            </ul>
          </div>
        </div>
      </UContainer>
    </section>

    <section
      id="use-cases"
      aria-labelledby="use-cases-title"
      class="scroll-mt-(--ui-header-height) border-y border-default bg-elevated/30"
    >
      <UContainer class="py-24 sm:py-32">
        <div class="mx-auto max-w-2xl text-center">
          <p class="font-semibold text-primary">
            Use cases
          </p>
          <h2
            id="use-cases-title"
            class="mt-2 text-3xl font-bold tracking-tight text-pretty text-highlighted sm:text-4xl"
          >
            Built for any organization that shares space
          </h2>
          <p class="mt-4 text-lg text-muted text-pretty">
            Wherever people take turns with the same rooms, courts and halls, Book A Space keeps it fair and clear.
          </p>
        </div>

        <ul class="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          <li
            v-for="useCase in useCases"
            :key="useCase.title"
            class="flex gap-4 rounded-2xl bg-default p-6 ring ring-default"
          >
            <UIcon
              :name="useCase.icon"
              class="mt-0.5 size-6 shrink-0 text-primary"
            />
            <div>
              <h3 class="font-semibold text-highlighted">
                {{ useCase.title }}
              </h3>
              <p class="mt-2 text-sm/6 text-muted">
                {{ useCase.description }}
              </p>
            </div>
          </li>
        </ul>
      </UContainer>
    </section>

    <section
      id="faq"
      aria-labelledby="faq-title"
      class="scroll-mt-(--ui-header-height)"
    >
      <UContainer class="grid gap-12 py-24 sm:py-32 lg:grid-cols-[2fr_3fr]">
        <div>
          <p class="font-semibold text-primary">
            FAQ
          </p>
          <h2
            id="faq-title"
            class="mt-2 text-3xl font-bold tracking-tight text-pretty text-highlighted sm:text-4xl"
          >
            Frequently asked questions
          </h2>
          <p class="mt-4 text-lg text-muted text-pretty">
            What people ask before they bring their facilities to Book A Space.
          </p>
        </div>

        <!-- Answers that are closed are still in the page, for search engines -->
        <UAccordion
          :items="faqs"
          :unmount-on-hide="false"
          type="multiple"
          :ui="{ trigger: 'text-base font-semibold text-highlighted py-5', body: 'text-base/7 text-muted pb-5' }"
        />
      </UContainer>
    </section>

    <UContainer class="pb-24 sm:pb-32">
      <UPageCTA
        title="Ready to stop double booking?"
        description="Bring every room, court and hall into one place, and let people book what's free in a few taps."
        :links="primary"
        variant="subtle"
        class="overflow-hidden"
        :ui="{ title: 'text-3xl sm:text-4xl font-bold tracking-tight', description: 'text-lg' }"
      >
        <template #top>
          <div
            class="absolute inset-0 -z-10 bg-[radial-gradient(70%_80%_at_50%_100%,color-mix(in_oklab,var(--ui-primary)_18%,transparent),transparent)]"
            aria-hidden="true"
          />
        </template>
      </UPageCTA>
    </UContainer>
  </div>
</template>
