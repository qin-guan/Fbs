import type { ButtonProps } from '@nuxt/ui'
import { useGetAuthMe } from '~/api'

/**
 * Where the buttons of the home page take somebody, which depends on how the build signs people in and on whether they are.
 *
 * The home page is drawn when the app is built (see routeRules in nuxt.config.ts), for nobody in particular, so who is signed in
 * is only looked at once it is in the browser. Until then, and in the HTML, it is somebody who isn't, and the page drawn in the
 * browser starts out the same as the one in the HTML.
 */
export function useHomeLinks() {
  const accounts = usesAccounts()
  const mounted = useMounted()
  const session = useAccountSession()
  const { data: me } = useGetAuthMe({ query: { enabled: computed(() => !accounts && mounted.value) } })

  const signedIn = computed(() => mounted.value && (accounts
    ? session.isLoaded.value && session.isSignedIn.value === true
    : !!me.value?.phone))

  /** The main buttons, at the top of the page and at its end */
  const primary = computed<ButtonProps[]>(() => {
    if (accounts) {
      return signedIn.value
        ? [{ label: 'Open your organizations', to: '/orgs', trailingIcon: 'i-lucide-arrow-right' }]
        : [
            { label: 'Get started', to: '/sign-up', trailingIcon: 'i-lucide-arrow-right' },
            { label: 'Sign in', to: '/sign-in', color: 'neutral', variant: 'subtle' },
          ]
    }

    return signedIn.value
      ? [
          { label: 'Make a booking', to: '/booking/new', icon: 'i-lucide-calendar-plus' },
          { label: 'View bookings', to: '/booking', color: 'neutral', variant: 'subtle' },
        ]
      : [{ label: 'Log in', to: '/auth/login', trailingIcon: 'i-lucide-arrow-right' }]
  })

  /** The buttons in the header, which is narrower */
  const header = computed<ButtonProps[]>(() => {
    if (signedIn.value) {
      return [{ label: 'Dashboard', to: accounts ? '/orgs' : '/booking', trailingIcon: 'i-lucide-arrow-right' }]
    }

    return accounts
      ? [
          { label: 'Sign in', to: '/sign-in', color: 'neutral', variant: 'ghost' },
          { label: 'Get started', to: '/sign-up' },
        ]
      : [{ label: 'Log in', to: '/auth/login', color: 'neutral', variant: 'outline' }]
  })

  return { signedIn, primary, header }
}
