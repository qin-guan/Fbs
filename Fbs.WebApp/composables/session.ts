import type { Ref } from 'vue'

export type AuthMode = 'workos' | 'clerk' | 'legacy' | 'test'

/** How this build signs people in, which is decided when it is built, see nuxt.config.ts. */
export function useAuthMode(): AuthMode {
  return useRuntimeConfig().public.authMode as AuthMode
}

/** Whether people have accounts and belong to organizations, rather than the old phone number pages. */
export function usesAccounts() {
  return useAuthMode() !== 'legacy'
}

/**
 * The session token to send to the API, or null when nobody is signed in. With Clerk or WorkOS it waits for them to load,
 * which is why requests made before that don't go out without one.
 */
export async function getSessionToken(mode: AuthMode): Promise<string | null> {
  if (mode === 'test') {
    return 'test-token'
  }

  if (mode === 'workos') {
    return await sessionActions.getToken?.() ?? null
  }

  if (mode === 'clerk') {
    // Only loaded when it is used, so the old sign in doesn't carry it
    const { getToken } = await import('@clerk/vue')
    return await getToken()
  }

  return null
}

export interface SessionState {
  isLoaded: boolean
  isSignedIn: boolean | undefined
  /** Who is signed in, for the account menu, with WorkOS. Clerk shows its own. */
  profile?: SessionProfile | null
}

export interface SessionProfile {
  name: string | null
  email: string
  pictureUrl: string | null
}

export type SignInScreen = 'sign-in' | 'sign-up'

/**
 * What can be done with the session, once Clerk or WorkOS has loaded. It isn't state, as it is only asked for by somebody who
 * clicks, or by a request. With WorkOS, `getToken` and `signIn` wait for it to load themselves.
 */
export const sessionActions: {
  signOut?: () => Promise<void>
  getToken?: () => Promise<string | null>
  signIn?: (returnTo: string, screen: SignInScreen) => Promise<void>
} = {}

export interface AccountSession {
  isLoaded: Readonly<Ref<boolean>>
  isSignedIn: Readonly<Ref<boolean | undefined>>
  profile: Readonly<Ref<SessionProfile | null>>
  signOut: () => Promise<void>
}

/**
 * Whether somebody is signed in with an account, and how they sign out. Only for builds with accounts. With Clerk or WorkOS
 * it follows what they say (see plugins/clerk-session.client.ts and plugins/workos-session.client.ts), and is not loaded until
 * they are.
 */
export function useAccountSession(): AccountSession {
  const mode = useAuthMode()
  const state = useState<SessionState>('account-session', () => ({
    isLoaded: mode !== 'clerk' && mode !== 'workos',
    isSignedIn: mode === 'test' ? true : undefined,
  }))

  return {
    isLoaded: computed(() => state.value.isLoaded),
    isSignedIn: computed(() => state.value.isSignedIn),
    profile: computed(() => state.value.profile ?? null),
    signOut: async () => {
      await sessionActions.signOut?.()
    },
  }
}
