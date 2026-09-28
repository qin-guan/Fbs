import type {
  DehydratedState,
  VueQueryPluginOptions,
} from '@tanstack/vue-query'
import {
  VueQueryPlugin,
  QueryClient,
  QueryCache,
  MutationCache,
  hydrate,
  dehydrate,
} from '@tanstack/vue-query'
import { ResponseError } from '~/api'

// The API answers a missing, expired or unreadable session cookie with a bare 401 (or 403).
// Endpoints that refuse a specific action, like cancelling another unit's booking, send a 403
// with problem details instead, which the page reports itself.
function isAuthError(error: unknown) {
  if (!(error instanceof ResponseError)) {
    return false
  }

  if (error.status === 401) {
    return true
  }

  return error.status === 403 && !error.contentType?.includes('problem+json')
}

export default defineNuxtPlugin((nuxt) => {
  const vueQueryState = useState<DehydratedState | null>('vue-query')
  const router = useRouter()

  const queryClient = new QueryClient({
    queryCache: new QueryCache({ onError: redirectToLogin }),
    mutationCache: new MutationCache({ onError: redirectToLogin }),
    defaultOptions: {
      queries: {
        staleTime: 5000,
        // Retrying won't bring the session back and would hold off the redirect for several seconds
        retry: (failureCount, error) => !isAuthError(error) && failureCount < 3,
      },
    },
  })
  const options: VueQueryPluginOptions = { queryClient }

  // Pages on the app layout need a session, so leave as soon as any request is rejected
  async function redirectToLogin(error: unknown) {
    if (!isAuthError(error) || router.currentRoute.value.meta.layout !== 'app') {
      return
    }

    // Drop data cached for the old session so the login page doesn't show the user as still logged in
    queryClient.clear()
    await router.push('/auth/login')
  }

  nuxt.vueApp.use(VueQueryPlugin, options)

  if (import.meta.server) {
    nuxt.hooks.hook('app:rendered', () => {
      vueQueryState.value = dehydrate(queryClient)
    })
  }

  if (import.meta.client) {
    hydrate(queryClient, vueQueryState.value)
  }
})
