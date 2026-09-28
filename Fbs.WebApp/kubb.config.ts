import { defineConfig } from 'kubb'
import { ast } from 'kubb/kit'
import { adapterOas } from '@kubb/adapter-oas'
import { pluginTs } from '@kubb/plugin-ts'
import { pluginFetch } from '@kubb/plugin-fetch'
import { pluginVueQuery } from '@kubb/plugin-vue-query'

// FastEndpoints names each operation after its endpoint class, like
// FbsWebApiEndpointsBookingByIdGetEndpoint. Name them getBookingById instead, so the hook is
// useGetBookingById and the response types are GetBookingById*.
const operationName = ast.defineMacro({
  name: 'operation-name',
  operation(node) {
    const match = /^FbsWebApiEndpoints(\w+?)(Get|Post|Put|Patch|Delete)Endpoint$/.exec(node.operationId)
    if (!match) return undefined

    const [, resource, method] = match
    return { ...node, operationId: `${method!.toLowerCase()}${resource}` }
  },
})

export default defineConfig({
  // Start the API first, or set OPENAPI_URL to another copy of the spec
  input: process.env.OPENAPI_URL ?? 'http://localhost:5204/openapi/v1.json',
  output: {
    path: './api',
    clean: true,
    barrel: { type: 'named' },
  },
  adapter: adapterOas({
    // The client turns date strings back into dates (see plugins/api.ts)
    dateType: 'date',
  }),
  plugins: [
    pluginTs({ macros: [operationName] }),
    pluginFetch({ macros: [operationName] }),
    pluginVueQuery({ macros: [operationName], hooks: true }),
  ],
})
