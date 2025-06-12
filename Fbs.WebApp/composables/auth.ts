import {useMutation, useQuery} from '@tanstack/vue-query'

export function useLoginMutation() {
    return useMutation({
        mutationFn: $api.auth.login.post,
    })
}

export function useVerifyMutation() {
    return useMutation({
        mutationFn: $api.auth.verify.post,
    })
}

export function useMe() {
    return useQuery({
        queryKey: ['me'],
        queryFn: () => $api.auth.me.get(),
        onSuccess(data) {
            if (window.clarity) {
                window.clarity("identify", data.phone)
            }
        }
    })
}
