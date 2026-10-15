// What the site says about itself, to people and to search engines and link previews, in one place: nuxt.config.ts, app.vue, the home page
// and robots.txt read it. Where it is reached is NUXT_PUBLIC_SITE_URL (runtimeConfig.public.siteUrl).
export const site = {
  name: 'Book A Space',
  /** The home page's title, which says what it is to somebody who has never heard of it */
  title: 'Book A Space: facility and room booking for shared spaces',
  /** Search engines show about 160 characters of it */
  description: 'Facility booking for organizations that share space. See what\'s free and book rooms, courts and halls in a few taps, with no double bookings.',
  /** What the picture link previews show (public/og-image.png, 1200 by 630) says */
  imageAlt: 'Book A Space: facility booking for teams that share spaces',
}

/** The pages behind a sign in, which are of no use to a search engine: robots.txt asks them not to be crawled, and they are sent with noindex. */
export const privatePaths = ['/t/', '/account', '/orgs', '/onboarding', '/join/', '/claim/', '/sign-in', '/sign-up', '/auth/', '/booking', '/profile']
