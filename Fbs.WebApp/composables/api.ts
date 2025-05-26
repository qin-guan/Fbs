import { AnonymousAuthenticationProvider } from '@microsoft/kiota-abstractions'
import { FetchRequestAdapter, HttpClient } from '@microsoft/kiota-http-fetchlibrary'
import { createApiClient } from '~/api/apiClient'

const authProvider = new AnonymousAuthenticationProvider()
const httpClient = new HttpClient((req, init) => fetch(req, { ...init, credentials: 'include' }))
const adapter = new FetchRequestAdapter(authProvider, undefined, undefined, httpClient)
adapter.baseUrl = useRuntimeConfig().public.api
export const $api = createApiClient(adapter)
