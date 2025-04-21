import urql, { cacheExchange, fetchExchange } from '@urql/vue';

export default defineNuxtPlugin(({ vueApp }) => {
    const { public: { api } } = useRuntimeConfig()

    vueApp.use(urql, {
        url: `${api}/graphql`,
        exchanges: [cacheExchange, fetchExchange],
        fetchOptions() {
            return {
                credentials: 'include'
            }
        }
    })
})
